using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Deliveries.Integration;

/// <summary>
/// A shipment left: open a delivery for each of its drops, ready for the driver. Idempotent (a re-delivered event adds nothing), and reads the shipment through the
/// shared contract so nothing here points into Shipments.
/// </summary>
internal sealed class ShipmentDispatchedSubscriber(
    DeliveriesDbContext db, IShipmentDeliveryFeed feed, ITransporterDirectory transporters, ISequenceGenerator sequences, TimeProvider clock) : IDomainEventHandler<ShipmentDispatched>
{
    public async Task HandleAsync(ShipmentDispatched e, CancellationToken cancellationToken)
    {
        var plan = await feed.GetAsync(e.ShipmentId, cancellationToken);
        if (plan is null || plan.Drops.Count == 0)
        {
            return;
        }

        var known = (await db.Deliveries.AsNoTracking().Where(d => d.ShipmentId == e.ShipmentId).Select(d => d.OrderId).ToListAsync(cancellationToken)).ToHashSet();
        var transporter = (await transporters.GetAsync([plan.TransporterId], cancellationToken)).GetValueOrDefault(plan.TransporterId);
        var actor = new Actor(null, null);
        var created = 0;

        foreach (var drop in plan.Drops.Where(d => !known.Contains(d.OrderId)))
        {
            var quantity = drop.Packages is > 0 ? drop.Packages.Value : drop.WeightKg;
            var unit = drop.Packages is > 0 ? "PKG" : "KG";
            var deliverBy = drop.DeliverBy ?? plan.PlannedPickupDate.AddDays(2);
            var windowEnd = new DateTimeOffset(deliverBy.ToDateTime(drop.WindowTo ?? new TimeOnly(18, 0)), Clock.India);
            var windowStart = drop.WindowFrom is { } from ? new DateTimeOffset(deliverBy.ToDateTime(from), Clock.India) : (DateTimeOffset?)null;

            var header = new Delivery.Header(
                plan.ShipmentId, plan.ShipmentNumber, drop.OrderId, drop.OrderReference ?? drop.OrderNumber, null, null, drop.LrNumber, drop.Sequence,
                plan.TransporterId, transporter?.LegalName, plan.VehicleId, plan.VehicleRegistration, plan.DriverName, null, drop.CustomerName, drop.CustomerPhone, null,
                plan.OriginCity, drop.City, $"{drop.AddressLine}, {drop.City}, {drop.State} {drop.Pincode}", drop.Latitude, drop.Longitude, null, windowEnd, windowStart, windowEnd);
            var number = $"DLV-{await sequences.NextAsync(e.TenantId, "delivery", cancellationToken):D5}";
            var delivery = Delivery.Create(e.TenantId, number, header, [new Delivery.ItemInput(drop.OrderReference ?? drop.OrderNumber, drop.Description, quantity, quantity, unit)], actor, clock.GetUtcNow());
            if (delivery.IsSuccess)
            {
                db.Deliveries.Add(delivery.Value);
                created++;
            }
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
