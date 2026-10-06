using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Tracking.Integration;

/// <summary>Used until planning provides the real thing: it knows of no trips, so a trip can only be tracked once it has been given to Tracking some other way (the demo seeder does).</summary>
internal sealed class LocalTrackingPlanningIntegration : ITrackingPlanningIntegration
{
    public Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(string tripReference, CancellationToken cancellationToken) => Task.FromResult<PlannedTrackingContext?>(null);

    public Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(Guid shipmentId, CancellationToken cancellationToken) => Task.FromResult<PlannedTrackingContext?>(null);
}

/// <summary>A shipment left: open its tracking record, ready for the driver to start. Idempotent: a trip is opened once.</summary>
internal sealed class ShipmentDispatchedSubscriber(ITrackingPlanningIntegration planning, TrackedShipmentFactory factory, TrackingDbContext db) : IDomainEventHandler<ShipmentDispatched>
{
    public async Task HandleAsync(ShipmentDispatched e, CancellationToken cancellationToken)
    {
        var plan = await planning.GetPlannedTrackingContextAsync(e.ShipmentId, cancellationToken);
        if (plan is null)
        {
            return;
        }

        var created = await factory.CreateAsync(plan, cancellationToken);
        if (created.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>The last drop has been delivered: the trip is over, and so is tracking.</summary>
internal sealed class ShipmentDeliveredSubscriber(TrackingDbContext db, SessionService sessions, TimeProvider clock) : IDomainEventHandler<ShipmentDelivered>
{
    public async Task HandleAsync(ShipmentDelivered e, CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.ShipmentId == e.ShipmentId, cancellationToken);
        if (shipment is null || shipment.Execution is ExecutionStatus.Completed or ExecutionStatus.Cancelled)
        {
            return;
        }

        var now = clock.GetUtcNow();
        shipment.SetDelivery(TrackedDeliveryStatus.Delivered);
        shipment.SetExecution(ExecutionStatus.Delivered);
        foreach (var milestone in await db.Milestones.Where(m => m.TrackedShipmentId == shipment.Id && m.Type == MilestoneType.Delivered && m.Status != MilestoneStatus.Achieved).ToListAsync(cancellationToken))
        {
            milestone.Achieve(e.DeliveredAt, EventSource.Pod);
        }

        db.Events.Add(ShipmentEvent.Create(shipment, ShipmentEventTypes.Delivered, "Delivered", e.DeliveredAt, EventSource.Pod));
        var session = shipment.CurrentSessionId is { } id ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) : null;
        if (session is { IsOpen: true })
        {
            await sessions.CloseAsync(shipment, session, "Delivered", completed: true, cancellationToken);
        }
        else
        {
            shipment.StopTracking(now, "Delivered", completed: true);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>One drop has been delivered. The trip is delivered once every drop is; until then it is partly delivered.</summary>
internal sealed class DeliveryCompletedSubscriber(TrackingDbContext db) : IDomainEventHandler<DeliveryCompleted>
{
    public async Task HandleAsync(DeliveryCompleted e, CancellationToken cancellationToken)
    {
        if (e.ShipmentId is not { } shipmentId)
        {
            return;
        }

        var shipment = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.ShipmentId == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return;
        }

        var drops = shipment.Stops.Where(s => s.Kind == StopKind.Drop).ToList();
        var stop = drops.FirstOrDefault(s => e.OrderId is not null && s.OrderId == e.OrderId);
        var milestones = await db.Milestones.Where(m => m.TrackedShipmentId == shipment.Id && m.Type == MilestoneType.Delivered).ToListAsync(cancellationToken);
        var milestone = milestones.FirstOrDefault(m => stop is not null && m.StopId == stop.Id);
        if (milestone is { Status: not MilestoneStatus.Achieved })
        {
            milestone.Achieve(e.DeliveredAt, EventSource.Pod);
            db.Events.Add(ShipmentEvent.Create(shipment, ShipmentEventTypes.Delivered, $"Delivered at {stop!.Name}", e.DeliveredAt, EventSource.Pod, stopId: stop.Id));
        }

        var delivered = milestones.Count(m => m.Status == MilestoneStatus.Achieved);
        shipment.SetDelivery(drops.Count > 0 && delivered >= drops.Count ? TrackedDeliveryStatus.Delivered : delivered > 0 ? TrackedDeliveryStatus.PartlyDelivered : TrackedDeliveryStatus.Pending);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>What a claim can use from the journey. Implemented here, read by a future Claims module through the shared contract.</summary>
internal sealed class TrackingClaimsEvidence(TrackingDbContext db) : ITrackingClaimsIntegration
{
    public async Task<TrackingEvidence?> GetEvidenceAsync(string tripReference, CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Stops).FirstOrDefaultAsync(s => s.TripReference == tripReference, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        var deviations = await db.Deviations.AsNoTracking().Where(d => d.TrackedShipmentId == shipment.Id).OrderBy(d => d.DetectedAt).ToListAsync(cancellationToken);
        var dwells = await db.Dwells.AsNoTracking().Where(d => d.TrackedShipmentId == shipment.Id).OrderBy(d => d.StartAt).ToListAsync(cancellationToken);
        var count = await db.Locations.AsNoTracking().CountAsync(l => l.ShipmentId == shipment.ShipmentId && l.Validation == LocationValidation.Valid, cancellationToken);
        var path = await db.Locations.AsNoTracking().Where(l => l.ShipmentId == shipment.ShipmentId && l.Validation == LocationValidation.Valid && !l.IsLate).OrderBy(l => l.CapturedAt)
            .Select(l => new TrackingPoint(l.Latitude, l.Longitude)).Take(5000).ToListAsync(cancellationToken);
        return new TrackingEvidence(
            shipment.TripReference, shipment.ShipmentReference, shipment.VehicleReference ?? string.Empty, shipment.StartedAt, shipment.CompletedAt, shipment.PlannedDistanceKm ?? 0, (decimal)Math.Round(shipment.TravelledKm, 1), count,
            deviations.Select(d => $"{d.DetectedAt:u}: {d.DistanceFromRouteKm:0.#} km off the route for {d.DurationMinutes} min ({d.Status})").ToList(),
            dwells.Select(d => $"{d.StartAt:u}: {(d.Kind == DwellKind.PlannedStop ? "stop" : "unplanned stop")} {d.Place} for {d.DurationMinutes} min").ToList(),
            shipment.Stops.Where(s => s.ArrivedAt is not null).OrderBy(s => s.ArrivedAt).Select(s => $"{s.ArrivedAt:u}: arrived at {s.Name}").ToList(), path);
    }
}
