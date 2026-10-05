using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Integration;

/// <summary>What the Deliveries module needs to open a delivery for each drop of a dispatched shipment, read from Shipments rather than copied.</summary>
internal sealed class ShipmentDeliveryFeed(ShipmentsDbContext db) : IShipmentDeliveryFeed
{
    public async Task<DeliveryPlanFact?> GetAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Orders).FirstOrDefaultAsync(s => s.Id == shipmentId && s.TransporterId != null, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        var orderIds = shipment.Orders.Where(o => !o.IsReturn).Select(o => o.OrderId).ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var locationIds = orders.Values.Where(o => o.DropLocationId != null).Select(o => o.DropLocationId!.Value).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);

        var drops = shipment.Orders.Where(o => !o.IsReturn && orders.ContainsKey(o.OrderId)).OrderBy(o => o.DropSequence).Select(link =>
        {
            var o = orders[link.OrderId];
            var location = o.DropLocationId is { } id ? locations.GetValueOrDefault(id) : null;
            return new DeliveryDropFact(
                o.Id, o.Number, o.Reference, link.DropSequence, link.LrNumber, o.Drop.Name, o.Drop.ContactPhone, o.Drop.Line1, o.Drop.City, o.Drop.State, o.Drop.Pincode,
                location?.Latitude, location?.Longitude, o.Description, o.Packages, o.WeightKg, o.DeliverByDate, o.DeliveryWindowFrom, o.DeliveryWindowTo);
        }).ToList();

        return new DeliveryPlanFact(shipment.Id, shipment.Number, shipment.TransporterId!.Value, shipment.VehicleId, shipment.VehicleRegistration, shipment.DriverName, shipment.OriginCity, shipment.PlannedPickupDate, drops);
    }
}
