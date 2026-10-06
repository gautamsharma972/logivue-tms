using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Queries;

/// <summary>
/// What an operator may correct by hand: the arrival estimate, a milestone, the reason for a delay or a route deviation. Every correction needs a reason, is written to the trip's
/// timeline and the audit trail, and sits beside what the system calculated, never over it.
/// </summary>
internal sealed class OverrideHandler(
    TrackingDbContext db, TrackingAccess access, ITrackingContextLoader loader, IShipmentEtaService eta, IMilestoneDetectionService milestones, Timeline timeline, PendingEvents pending, ShipmentQueryHandler query,
    ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<EtaDto>> OverrideEtaAsync(Guid id, OverrideEtaRequest request, CancellationToken cancellationToken)
    {
        var found = await Load(id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Reason("Say why the estimate is being changed.");
        }

        var shipment = found.Value;
        var now = clock.GetUtcNow();
        if (request.Eta < now.AddDays(-1) || request.Eta > now.AddDays(30))
        {
            return Error.Validation("tracking.eta_out_of_range", "The arrival must be within the next 30 days.");
        }

        var system = shipment.SystemEtaAt;
        shipment.OverrideEta(request.Eta, request.Reason, user.UserId, now);
        timeline.Add(shipment, ShipmentEventTypes.EtaOverride,
            $"Expected arrival set to {Timeline.Time(request.Eta)} by an operator (system said {(system is { } s ? Timeline.Time(s) : "nothing")}): {request.Reason.Trim()}", now, EventSource.Manual, reason: request.Reason.Trim(), by: user.UserId);
        await RecalculateAsync(shipment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await query.EtaAsync(id, cancellationToken);
    }

    public async Task<Result<EtaDto>> ClearEtaOverrideAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await Load(id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;
        shipment.ClearEtaOverride();
        timeline.Add(shipment, ShipmentEventTypes.EtaOverride, "The operator's arrival estimate was removed; the calculated one applies again.", clock.GetUtcNow(), EventSource.Manual, by: user.UserId);
        await RecalculateAsync(shipment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await query.EtaAsync(id, cancellationToken);
    }

    /// <summary>Recalculates arrivals and risk now, for people who changed something the engine cannot see (a delivery window, a plan).</summary>
    public async Task<Result<EtaDto>> RecalculateAsync(RecalculateEtaRequest request, CancellationToken cancellationToken)
    {
        var shipment = request.ShipmentId is { } id
            ? await Load(id, cancellationToken)
            : await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.TripReference == request.TripReference, cancellationToken) is { } byTrip && access.CanSee(byTrip) && access.CanManage ? byTrip : TrackingAccess.ShipmentNotFound;
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        if (shipment.Value.LastLatitude is null)
        {
            return Error.Conflict("tracking.no_location", "There is no location to estimate from yet.");
        }

        await RecalculateAsync(shipment.Value, cancellationToken, force: true);
        await db.SaveChangesAsync(cancellationToken);
        return await query.EtaAsync(shipment.Value.Id, cancellationToken);
    }

    public async Task<Result<TimelineEntryDto>> ManualMilestoneAsync(Guid id, ManualMilestoneRequest request, CancellationToken cancellationToken)
    {
        var found = await Load(id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Reason("Say why the milestone is being recorded by hand.");
        }

        var shipment = found.Value;
        var now = clock.GetUtcNow();
        var at = request.At ?? now;
        if (at > now.AddMinutes(5))
        {
            return Error.Validation("tracking.future_milestone", "A milestone cannot be in the future.");
        }

        var stop = request.StopId is { } stopId ? shipment.Stops.FirstOrDefault(s => s.Id == stopId) : null;
        if (request.StopId is not null && stop is null)
        {
            return Error.Validation("tracking.unknown_stop", "That stop is not part of this trip.");
        }

        var session = shipment.CurrentSessionId is { } sid ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == sid, cancellationToken) : await db.Sessions.OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(s => s.TrackedShipmentId == shipment.Id, cancellationToken);
        var context = await loader.LoadAsync(shipment, session ?? TrackingSession.Start(shipment.TenantId, "TS-MANUAL", shipment, null, null, null, user.UserId, now), now, cancellationToken);
        milestones.Achieve(context, request.Type, stop?.Id, at, EventSource.Manual, request.Reason.Trim());

        switch (request.Type)
        {
            case MilestoneType.ArrivedOrigin or MilestoneType.ArrivedStop or MilestoneType.ArrivedDestination when stop is not null:
                stop.Arrive(at);
                break;
            case MilestoneType.DepartedOrigin or MilestoneType.DepartedStop when stop is not null:
                stop.Depart(at);
                break;
            case MilestoneType.DepartedOrigin:
                shipment.SetExecution(ExecutionStatus.Departed);
                break;
            case MilestoneType.InTransit:
                shipment.SetExecution(ExecutionStatus.InTransit);
                break;
            case MilestoneType.LoadingStarted:
                shipment.SetExecution(ExecutionStatus.Loading);
                break;
            case MilestoneType.Delivered when stop is not null:
                stop.Depart(at);
                break;
        }

        var entry = timeline.Add(shipment, ShipmentEventTypes.ManualMilestone, $"{request.Type} recorded by an operator{(stop is null ? string.Empty : $" at {stop.Name}")}: {request.Reason.Trim()}", at, EventSource.Manual,
            stopId: stop?.Id, reason: request.Reason.Trim(), by: user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new TimelineEntryDto(entry.EventTime, "Actual", entry.Description, null, "Manual", entry.EventType, null, null, entry.StopId, entry.ReasonCode);
    }

    public async Task<Result<TrackedShipmentDto>> SetDelayReasonAsync(Guid id, SetDelayReasonRequest request, CancellationToken cancellationToken)
    {
        var found = await Load(id, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.SetDelayReason(request.Reason, request.Note);
        timeline.Add(found.Value, ShipmentEventTypes.DelayReason, $"Delay reason recorded: {request.Reason}{(string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" ({request.Note.Trim()})")}", clock.GetUtcNow(), EventSource.Manual,
            reason: request.Reason.ToString(), by: user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await query.GetAsync(id, cancellationToken);
    }

    public async Task<Result<RouteDeviationDto>> DeviationReasonAsync(Guid deviationId, DeviationReasonRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return TrackingAccess.Forbidden;
        }

        var deviation = await db.Deviations.FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken);
        if (deviation is null)
        {
            return TrackingAccess.ShipmentNotFound;
        }

        deviation.RecordReason(request.Reason, request.Note);
        await db.SaveChangesAsync(cancellationToken);
        return new RouteDeviationDto(deviation.Id, deviation.DetectedAt, deviation.Latitude, deviation.Longitude, deviation.DistanceFromRouteKm, deviation.DurationMinutes, deviation.Severity, deviation.Status, deviation.Reason, deviation.ReasonNote, deviation.ResolvedAt);
    }

    private async Task RecalculateAsync(TrackedShipment shipment, CancellationToken cancellationToken, bool force = false)
    {
        var now = clock.GetUtcNow();
        var session = shipment.CurrentSessionId is { } sid ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == sid, cancellationToken) : await db.Sessions.OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(s => s.TrackedShipmentId == shipment.Id, cancellationToken);
        if (session is null || shipment.LastLatitude is null)
        {
            return;
        }

        var context = await loader.LoadAsync(shipment, session, now, cancellationToken);
        await eta.UpdateAsync(context, force, cancellationToken);
        foreach (var e in pending.Drain())
        {
            shipment.Publish(e);
        }
    }

    private async Task<Result<TrackedShipment>> Load(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return TrackingAccess.Forbidden;
        }

        var shipment = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.Id == id || s.ShipmentId == id, cancellationToken);
        return shipment is null ? TrackingAccess.ShipmentNotFound : shipment;
    }

    private static Error Reason(string message) => Error.Validation("tracking.reason_required", message) with { ValidationErrors = new Dictionary<string, string[]> { ["reason"] = [message] } };
}
