using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Deliveries.Integration;

/// <summary>
/// Tracking saw a vehicle reach a customer's site. The delivery for that drop notes it (and takes it as the arrival time if the driver has not recorded one), so the proof of delivery
/// starts from when the vehicle really got there. Idempotent.
/// </summary>
internal sealed class DeliveryTrackingSiteSubscriber(DeliveriesDbContext db) : IDomainEventHandler<DeliveryTrackingEvent>
{
    public async Task HandleAsync(DeliveryTrackingEvent e, CancellationToken cancellationToken)
    {
        if (e.Kind != "ArrivedAtSite" || e.OrderId is not { } orderId)
        {
            return;
        }

        var delivery = await db.Deliveries.Include(d => d.Events).FirstOrDefaultAsync(d => d.ShipmentId == e.ShipmentId && d.OrderId == orderId, cancellationToken);
        if (delivery is null)
        {
            return;
        }

        delivery.NoteSiteArrival(e.At, e.Latitude is { } lat && e.Longitude is { } lon ? new GeoFix(lat, lon, null) : GeoFix.None);
        await db.SaveChangesAsync(cancellationToken);
    }
}
