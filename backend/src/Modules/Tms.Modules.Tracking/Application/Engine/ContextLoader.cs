using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;

namespace Tms.Modules.Tracking.Application.Engine;

internal interface ITrackingContextLoader
{
    Task<TrackingContext> LoadAsync(TrackedShipment shipment, TrackingSession session, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Gathers what the engine needs about one trip, in as few queries as it can.</summary>
internal sealed class TrackingContextLoader(TrackingDbContext db, ITrackingSettings settings) : ITrackingContextLoader
{
    public async Task<TrackingContext> LoadAsync(TrackedShipment shipment, TrackingSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var snapshot = await settings.SnapshotAsync(cancellationToken);
        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.TrackedShipmentId == shipment.Id, cancellationToken);
        var today = DateOnly.FromDateTime(now.ToOffset(Clock.India).DateTime);
        var geofences = (await db.Geofences.AsNoTracking().Where(g => g.Status == GeofenceStatus.Active).ToListAsync(cancellationToken)).Where(g => g.IsActiveOn(today)).ToList();
        var presences = await db.Presences.Where(p => p.TrackedShipmentId == shipment.Id).ToDictionaryAsync(p => p.SubjectId, cancellationToken);
        var milestones = await db.Milestones.Where(m => m.TrackedShipmentId == shipment.Id).OrderBy(m => m.Order).ToListAsync(cancellationToken);
        var alerts = await db.Alerts.Where(a => a.TrackedShipmentId == shipment.Id).ToListAsync(cancellationToken);
        var exceptions = await db.Exceptions.Where(e => e.TrackedShipmentId == shipment.Id && e.Status != ExceptionStatus.Closed).ToListAsync(cancellationToken);
        var deviation = await db.Deviations.Where(d => d.TrackedShipmentId == shipment.Id && d.Status == DeviationStatus.Open).FirstOrDefaultAsync(cancellationToken);
        var dwell = await db.Dwells.Where(d => d.TrackedShipmentId == shipment.Id && d.Status == DwellStatus.Ongoing).FirstOrDefaultAsync(cancellationToken);
        var gap = await db.Gaps.Where(g => g.TrackedShipmentId == shipment.Id && g.GapEnd == null).FirstOrDefaultAsync(cancellationToken);

        return new TrackingContext
        {
            TenantId = shipment.TenantId, Shipment = shipment, Session = session, Settings = snapshot, Now = now,
            Route = route is null ? null : new RouteGeometry(route.Points), Stops = shipment.Stops.OrderBy(s => s.Sequence).ToList(), SharedGeofences = geofences, Presences = presences,
            Milestones = milestones, Alerts = alerts, Exceptions = exceptions, OpenDeviation = deviation, OpenDwell = dwell, OpenGap = gap,
        };
    }
}
