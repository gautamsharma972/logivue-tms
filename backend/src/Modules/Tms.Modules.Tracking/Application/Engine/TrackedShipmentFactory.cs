using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Engine;

/// <summary>Opens the tracking record of a trip from what planning says about it. Safe to call twice: a trip is tracked once.</summary>
internal sealed class TrackedShipmentFactory(TrackingDbContext db, ITrackingPlanningIntegration planning, ITrackingSettings settings, Timeline timeline, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<TrackedShipment>> EnsureAsync(string tripReference, CancellationToken cancellationToken)
    {
        var existing = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.TripReference == tripReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var plan = await planning.GetPlannedTrackingContextAsync(tripReference, cancellationToken);
        return plan is null ? TrackingAccess.ShipmentNotFound : await CreateAsync(plan, cancellationToken);
    }

    public async Task<Result<TrackedShipment>> CreateAsync(PlannedTrackingContext plan, CancellationToken cancellationToken)
    {
        var tenantId = user.TenantId ?? throw new InvalidOperationException("Tracking needs a tenant.");
        var existing = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.ShipmentId == plan.ShipmentId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var snapshot = await settings.SnapshotAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var shipment = TrackedShipment.Create(tenantId, plan, snapshot.Geofence.DefaultRadiusM);

        // The route to follow: the planned geometry when there is one, otherwise a straight line through the stops, honestly labelled an estimate.
        var points = plan.Route.Select(p => new GeoPoint(p.Latitude, p.Longitude)).ToList();
        var source = plan.RouteSource;
        if (points.Count < 2)
        {
            points = shipment.Stops.Where(s => s.HasLocation).OrderBy(s => s.Sequence).Select(s => s.Point!.Value).ToList();
            source = "Estimate";
        }

        if (points.Count >= 2)
        {
            db.Routes.Add(RoutePlan.Create(tenantId, shipment.Id, points, plan.PlannedDurationMinutes, source, now));
            var geometry = new RouteGeometry(points);
            double cursor = 0;
            foreach (var stop in shipment.Stops.OrderBy(s => s.Sequence).Where(s => s.HasLocation))
            {
                var match = geometry.Match(stop.Point!.Value, cursor);
                stop.SetAlong(Math.Round(match.AlongKm, 3));
                cursor = match.AlongKm;
            }
        }

        // A stop that sits inside a shared geofence (a warehouse outline, a customer's yard) is judged by that outline rather than a circle.
        var today = DateOnly.FromDateTime(now.ToOffset(Clock.India).DateTime);
        var shared = (await db.Geofences.AsNoTracking().Where(g => g.Status == GeofenceStatus.Active).ToListAsync(cancellationToken)).Where(g => g.IsActiveOn(today)).ToList();
        foreach (var stop in shipment.Stops.Where(s => s.HasLocation))
        {
            var match = shared.Where(g => g.Type is GeofenceType.Warehouse or GeofenceType.Customer or GeofenceType.Hub or GeofenceType.CrossDock or GeofenceType.Depot or GeofenceType.Origin or GeofenceType.Destination)
                .FirstOrDefault(g => g.Shape.Contains(stop.Point!.Value));
            if (match is not null)
            {
                stop.UseGeofence(match.Id, match.Type);
            }
        }

        db.Shipments.Add(shipment);
        foreach (var milestone in MilestoneService.CreateFor(shipment, snapshot.Milestones, now))
        {
            db.Milestones.Add(milestone);
        }

        timeline.Add(shipment, ShipmentEventTypes.Dispatched, $"Ready to track: {plan.VehicleReference ?? "vehicle to be assigned"}, {shipment.Stops.Count} stop(s)", now, EventSource.Planning);
        return shipment;
    }
}
