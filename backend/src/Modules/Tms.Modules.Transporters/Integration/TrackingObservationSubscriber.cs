using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Transporters.Integration;

/// <summary>Keeps what Tracking saw on a carrier's trips. Idempotent: an event delivered twice adds nothing.</summary>
internal sealed class TrackingObservationSubscriber(TransportersDbContext db) : IDomainEventHandler<TrackingPerformanceEvent>
{
    public async Task HandleAsync(TrackingPerformanceEvent e, CancellationToken cancellationToken)
    {
        if (e.TransporterId is not { } carrier || await db.TrackingObservations.AnyAsync(o => o.SourceEventId == e.EventId, cancellationToken))
        {
            return;
        }

        db.TrackingObservations.Add(TrackingObservation.Create(e.TenantId, e.EventId, carrier, e.ShipmentReference, e.TripReference, e.Kind, e.Value, e.Detail, e.At));
        await db.SaveChangesAsync(cancellationToken);
    }
}
