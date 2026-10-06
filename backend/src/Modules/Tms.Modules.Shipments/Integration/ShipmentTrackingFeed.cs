using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Integration;

/// <summary>
/// What Tracking needs to follow a trip, read from Shipments rather than copied: who carries it, where its stops are, when each should be reached, and the road between them. The
/// route is the road geometry when the routing provider can draw one; otherwise Tracking is told it is only an estimate.
/// </summary>
internal sealed class ShipmentTrackingFeed(ShipmentsDbContext db, IRoutingProvider routing, ITransporterDirectory transporters) : ITrackingPlanningIntegration
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public async Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(string tripReference, CancellationToken cancellationToken)
    {
        var id = await db.Shipments.AsNoTracking().Where(s => s.Number == tripReference && s.TransporterId != null).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
        return id is null ? null : await GetPlannedTrackingContextAsync(id.Value, cancellationToken);
    }

    public async Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Orders).FirstOrDefaultAsync(s => s.Id == shipmentId && s.TransporterId != null, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        var orderIds = shipment.Orders.Select(o => o.OrderId).ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var locationIds = orders.Values.SelectMany(o => new[] { o.PickupLocationId, o.DropLocationId }).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);

        var stops = new List<TrackingStopFact>();
        var sequence = 0;
        var pickupDay = shipment.PlannedPickupDate;
        foreach (var pickup in shipment.Orders.Where(o => !o.IsReturn && orders.ContainsKey(o.OrderId)).Select(o => orders[o.OrderId])
            .GroupBy(o => o.PickupLocationId?.ToString() ?? $"{o.PickupState}|{o.PickupCity}|{o.Pickup.Line1}").Select(g => g.First()))
        {
            var location = pickup.PickupLocationId is { } pid ? locations.GetValueOrDefault(pid) : null;
            var from = pickup.PickupWindowFrom ?? new TimeOnly(9, 0);
            stops.Add(new TrackingStopFact(
                null, ++sequence, "Pickup", location?.Name ?? pickup.Pickup.Name, pickup.Pickup.City, location?.Latitude, location?.Longitude, At(pickupDay, from), pickup.PickupWindowFrom is null ? null : At(pickupDay, pickup.PickupWindowFrom.Value),
            pickup.PickupWindowTo is null ? null : At(pickupDay, pickup.PickupWindowTo.Value), null, pickup.Number, null, pickup.Pickup.Name));
        }

        foreach (var link in shipment.Orders.Where(o => !o.IsReturn && orders.ContainsKey(o.OrderId)).OrderBy(o => o.DropSequence))
        {
            var order = orders[link.OrderId];
            var location = order.DropLocationId is { } did ? locations.GetValueOrDefault(did) : null;
            var day = order.DeliverByDate ?? pickupDay.AddDays(2);
            var due = At(day, order.DeliveryWindowTo ?? new TimeOnly(18, 0));
            stops.Add(new TrackingStopFact(
                order.Id, ++sequence, "Drop", location?.Name ?? order.Drop.Name, order.Drop.City, location?.Latitude, location?.Longitude, due, order.DeliveryWindowFrom is { } wf ? At(day, wf) : null,
                order.DeliveryWindowTo is null ? null : due, null, order.Reference ?? order.Number, order.Reference, order.Drop.Name));
        }

        // The road between the stops that have a position, in the order they are visited.
        var waypoints = stops.Where(s => s.Latitude is not null && s.Longitude is not null).Select(s => new GeoPoint(s.Latitude!.Value, s.Longitude!.Value)).ToList();
        IReadOnlyList<TrackingPoint> route = [];
        string source = "Estimate";
        decimal? km = shipment.DistanceKm;
        int? minutes = null;
        if (waypoints.Count >= 2)
        {
            var result = await routing.GetRouteAsync(waypoints, cancellationToken);
            km ??= (decimal)Math.Round(result.TotalKm, 1);
            minutes = (int)Math.Round(result.TotalMinutes);
            if (result.Source == RouteSource.Osrm && await routing.GetGeometryAsync(waypoints, cancellationToken) is { Count: >= 2 } geometry)
            {
                route = geometry.Select(p => new TrackingPoint(p.Latitude, p.Longitude)).ToList();
                source = "Osrm";
            }
        }

        var name = (await transporters.GetAsync([shipment.TransporterId!.Value], cancellationToken)).GetValueOrDefault(shipment.TransporterId!.Value)?.LegalName;
        var start = stops.FirstOrDefault(s => s.Kind == "Pickup")?.PlannedArrival ?? At(pickupDay, new TimeOnly(9, 0));
        return new PlannedTrackingContext(
            shipment.Id, shipment.Number, shipment.Number, shipment.TransporterId.Value, name, shipment.VehicleRegistration, shipment.DriverName, shipment.DriverPhone, start, km, minutes, source, route, stops);
    }

    private static DateTimeOffset At(DateOnly day, TimeOnly time) => new(day.ToDateTime(time), India);
}
