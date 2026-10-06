using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Application.Performance;

/// <summary>
/// What Tracking saw on a carrier's trips. A null average means there was nothing to average, which is not zero. Tracking quality is reported on its own so it is never mistaken for performance.
/// </summary>
public sealed record TrackingPerformanceDto(
    DateOnly From, DateOnly To, int Trips, decimal? AveragePickupDelayMinutes, decimal? AverageDeliveryDelayMinutes, int LateDeliveries, int RouteDeviations, int ExcessDwells, int UnplannedStops,
    int TrackingLostIncidents, decimal? TrackingLostMinutes);

internal sealed class TrackingPerformanceHandler(TransportersDbContext db, PerformanceAccess access, TimeProvider clock)
{
    public async Task<Result<TrackingPerformanceDto>> HandleAsync(Guid transporterId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var end = to ?? clock.TodayInIndia();
        var start = from ?? end.AddDays(-90);
        var lower = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var upper = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var rows = await db.TrackingObservations.AsNoTracking().Where(o => o.TransporterId == transporterId && o.At >= lower && o.At < upper).ToListAsync(cancellationToken);

        static decimal? Average(IEnumerable<decimal?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count == 0 ? null : Math.Round(list.Average(), 1);
        }

        var pickups = rows.Where(r => r.Kind == "PickupDelay").ToList();
        var deliveries = rows.Where(r => r.Kind == "DeliveryDelay").ToList();
        var lost = rows.Where(r => r.Kind == "TrackingCompliance").ToList();
        return new TrackingPerformanceDto(
            start, end, rows.Select(r => r.TripReference).Distinct().Count(), Average(pickups.Select(r => r.Value)), Average(deliveries.Select(r => r.Value)), deliveries.Count(r => r.Value > 0),
            rows.Count(r => r.Kind == "RouteDeviation"), rows.Count(r => r.Kind == "ExcessDwell"), rows.Count(r => r.Kind == "UnplannedStop"), lost.Count, lost.Count == 0 ? null : lost.Sum(r => r.Value ?? 0));
    }
}
