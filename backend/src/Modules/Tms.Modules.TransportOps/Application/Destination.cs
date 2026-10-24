using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

/// <summary>Office records an arrival or unloading the driver could not (or an arrival that failed the GPS check). A reason is always required and the event says it came from the office.</summary>
public sealed record DestinationEventRequest(EventType Type, DateTimeOffset? ActualAt, string Reason);

public sealed record CorrectDestinationRequest(EventType Type, DateTimeOffset NewActualAt, string Reason);

internal sealed class DestinationEventRequestValidator : AbstractValidator<DestinationEventRequest>
{
    public DestinationEventRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t is EventType.DestinationArrival or EventType.UnloadingCompleted).WithMessage("Only arrival or unloading can be recorded here.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithMessage("A reason is required.");
    }
}

internal sealed class CorrectDestinationRequestValidator : AbstractValidator<CorrectDestinationRequest>
{
    public CorrectDestinationRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t is EventType.DestinationArrival or EventType.UnloadingCompleted).WithMessage("Only arrival or unloading times can be corrected here.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithMessage("A reason is required.");
    }
}

/// <summary>
/// The office's hand on the destination events. Arrival from the office needs the supervisor permission (it replaces the GPS check) and both kinds need a reason; each is kept as its own
/// event, so the history shows who did it and why.
/// </summary>
internal sealed class DestinationEventsHandler(TransportOpsDbContext db, OpsAccess access, LrHandler lrs, TimeProvider clock)
{
    private static readonly TimeSpan FutureSlack = TimeSpan.FromMinutes(5);

    public async Task<Result<LrDetailDto>> RecordAsync(Guid lrId, DestinationEventRequest request, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == lrId, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (request.Type == EventType.DestinationArrival && !access.Has(Domain.OpsPermissions.ArrivalOverride))
        {
            return Error.Forbidden("ops.arrival_override_forbidden", "Only a supervisor can record an arrival on the driver's behalf.");
        }

        var now = clock.GetUtcNow();
        var at = request.ActualAt ?? now;
        if (at > now + FutureSlack)
        {
            return Error.Validation("ops.event_future", "An event cannot be in the future.");
        }

        Result done;
        if (request.Type == EventType.DestinationArrival)
        {
            if (lr.DepartedAt is { } left && at < left)
            {
                return Error.Validation("ops.event_time_order", "The arrival cannot be before the vehicle left the CFA.");
            }

            done = lr.Arrive(at);
            if (done.IsSuccess && lr.TripStopId is { } stopId && await db.TripStops.FirstOrDefaultAsync(s => s.Id == stopId, cancellationToken) is { } stop)
            {
                stop.Arrived(at);
            }
        }
        else
        {
            if (lr.ArrivedAt is { } arrived && at < arrived)
            {
                return Error.Validation("ops.event_time_order", "Unloading cannot finish before the arrival.");
            }

            done = lr.CompleteUnloading(at);
            if (done.IsSuccess)
            {
                if (lr.TripStopId is { } stopId && await db.TripStops.FirstOrDefaultAsync(s => s.Id == stopId, cancellationToken) is { } stop)
                {
                    stop.Unloaded(at);
                    if (lr.TripId is { } tripId && await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken) is { } trip)
                    {
                        trip.CompleteIfAllUnloaded();
                    }
                }

                var pod = await db.Pods.FirstOrDefaultAsync(p => p.LrId == lr.Id, cancellationToken);
                if (pod is { Status: PodStatus.NotYetDue })
                {
                    pod.MarkPending(now);
                }
            }
        }

        if (done.IsFailure)
        {
            return done.Error;
        }

        var evidence = DeliveryEvent.Create(lr.TenantId, lr.TripId, lr.Id, request.Type, at, now, EventSource.Office, access.UserId, request.Reason, null);
        if (request.Type == EventType.DestinationArrival)
        {
            evidence.WithArrivalEvidence(null, null, null, null, null, null, 0, null, "OfficeOverride");
        }

        db.Events.Add(evidence);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("ops.event_duplicate", "That event has already been recorded for this LR.");
        }

        return await lrs.GetAsync(lr.Id, cancellationToken);
    }

    public async Task<Result<LrDetailDto>> CorrectAsync(Guid lrId, CorrectDestinationRequest request, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == lrId, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (await db.Calculations.AnyAsync(c => c.LrId == lrId && c.Status == CalculationStatus.Invoiced, cancellationToken))
        {
            return Error.Conflict("ops.invoiced", "This LR has been invoiced, so its times cannot be changed.");
        }

        var now = clock.GetUtcNow();
        if (request.NewActualAt > now + FutureSlack)
        {
            return Error.Validation("ops.event_future", "An event cannot be in the future.");
        }

        var current = request.Type == EventType.DestinationArrival ? lr.ArrivedAt : lr.UnloadedAt;
        if (current is null)
        {
            return Error.Conflict("ops.event_missing", "That event has not been recorded yet.");
        }

        if (request.Type == EventType.DestinationArrival)
        {
            if ((lr.DepartedAt is { } left && request.NewActualAt < left) || (lr.UnloadedAt is { } unloaded && request.NewActualAt > unloaded))
            {
                return Error.Validation("ops.event_time_order", "The arrival must be after the vehicle left the CFA and before unloading finished.");
            }
        }
        else if (lr.ArrivedAt is { } arrived && request.NewActualAt < arrived)
        {
            return Error.Validation("ops.event_time_order", "Unloading cannot finish before the arrival.");
        }

        lr.SetTime(request.Type, request.NewActualAt);
        if (lr.TripStopId is { } stopId && await db.TripStops.FirstOrDefaultAsync(s => s.Id == stopId, cancellationToken) is { } stop)
        {
            if (request.Type == EventType.DestinationArrival)
            {
                stop.CorrectArrived(request.NewActualAt);
            }
            else
            {
                stop.Unloaded(request.NewActualAt);
            }
        }

        var original = await db.Events.Where(e => e.LrId == lrId && e.Type == request.Type && e.CorrectsEventId == null).OrderByDescending(e => e.RecordedAt).FirstOrDefaultAsync(cancellationToken);
        db.Events.Add(DeliveryEvent.Create(lr.TenantId, lr.TripId, lr.Id, EventType.Correction, request.NewActualAt, now, EventSource.Office, access.UserId, $"{request.Type}: {request.Reason} (was {current:dd MMM yyyy HH:mm})", null, null, original?.Id));
        await db.SaveChangesAsync(cancellationToken);
        return await lrs.GetAsync(lr.Id, cancellationToken);
    }
}
