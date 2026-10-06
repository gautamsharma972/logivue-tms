using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;

namespace Tms.Modules.Tracking.Application.Engine;

/// <param name="Points">[latitude, longitude, epoch milliseconds, speed km/h (0 when unknown)] in time order.</param>
/// <param name="Source">"Raw" (every stored GPS point, thinned for display) or "Summary" (the simplified path kept after the raw points were purged).</param>
public sealed record ReplayDto(IReadOnlyList<double[]> Points, string Source, DateTimeOffset? From, DateTimeOffset? To, int StoredPoints);

/// <summary>
/// A trip's path after the fact. While the raw GPS exists it is the source; when a trip completes a simplified copy of its path is stored on the trip, so
/// replays, disputes and reports still work once the raw points have aged out.
/// </summary>
internal sealed class RouteHistory(TrackingDbContext db)
{
    private const double ToleranceMetres = 25;

    /// <summary>The trip's path as JSON [[lat, lon, epoch seconds], ...], or null when it has no usable points.</summary>
    public async Task<string?> BuildSummaryAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var rows = await db.Locations.AsNoTracking().Where(l => l.ShipmentId == shipmentId && l.Validation == LocationValidation.Valid && !l.IsLate).OrderBy(l => l.CapturedAt)
            .Select(l => new { l.Latitude, l.Longitude, l.CapturedAt }).Take(200_000).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var simplified = RouteSimplifier.Simplify(rows.Select(r => new PathPoint(r.Latitude, r.Longitude, r.CapturedAt.ToUnixTimeSeconds())).ToList(), ToleranceMetres);
        return JsonSerializer.Serialize(simplified.Select(p => new[] { Math.Round(p.Latitude, 5), Math.Round(p.Longitude, 5), (double)p.EpochSeconds }));
    }

    public async Task<ReplayDto> ReplayAsync(TrackedShipment shipment, DateTimeOffset? from, DateTimeOffset? to, int maxPoints, CancellationToken cancellationToken)
    {
        var max = Math.Clamp(maxPoints, 10, 5000);
        var raw = db.Locations.AsNoTracking().Where(l => l.ShipmentId == shipment.ShipmentId && l.Validation == LocationValidation.Valid && !l.IsLate);
        if (from is { } f)
        {
            raw = raw.Where(l => l.CapturedAt >= f);
        }

        if (to is { } t)
        {
            raw = raw.Where(l => l.CapturedAt <= t);
        }

        var rows = await raw.OrderBy(l => l.CapturedAt).Select(l => new { l.Latitude, l.Longitude, l.CapturedAt, l.SpeedKph }).Take(200_000).ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            var thinned = rows.Count <= max ? rows : Enumerable.Range(0, max).Select(i => rows[(int)Math.Round(i * (rows.Count - 1.0) / (max - 1))]).ToList();
            return new ReplayDto(thinned.Select(r => new[] { r.Latitude, r.Longitude, (double)r.CapturedAt.ToUnixTimeMilliseconds(), r.SpeedKph ?? 0 }).ToList(), "Raw", thinned[0].CapturedAt, thinned[^1].CapturedAt, rows.Count);
        }

        var summary = Parse(shipment.ActualRouteJson).Where(p => (from is null || p[2] * 1000 >= from.Value.ToUnixTimeMilliseconds()) && (to is null || p[2] * 1000 <= to.Value.ToUnixTimeMilliseconds())).ToList();
        var points = summary.Select(p => new[] { p[0], p[1], p[2] * 1000, 0d }).ToList();
        return new ReplayDto(points, points.Count == 0 ? "None" : "Summary", points.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)points[0][2]), points.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)points[^1][2]), 0);
    }

    public static List<double[]> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<double[]>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
