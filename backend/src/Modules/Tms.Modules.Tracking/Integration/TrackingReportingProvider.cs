using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Tracking.Integration;

/// <summary>
/// What Reports &amp; Analytics reads from tracking: trips with their health, risk, ETA, distance and dwell, deviations, dwell events, gaps and exceptions. Health is
/// brought up to date first (stale and lost are judged on read), so a lost phone is reported as lost, never as parked. Read-only.
/// </summary>
internal sealed class TrackingReportingProvider(TrackingDbContext db, TrackingHealthMonitor monitor, ITransporterDirectory transporters) : ITrackingReportingProvider
{
    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    private static DateTimeOffset Start(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken) =>
        (await transporters.GetAsync(ids.Where(i => i is not null).Select(i => i!.Value).Distinct(), cancellationToken)).ToDictionary(t => t.Key, t => t.Value.LegalName);

    private static string Lane(TrackedShipment t) => $"{t.OriginName ?? "Origin"} → {t.DestinationName ?? "Destination"}";

    public async Task<IReadOnlyList<TrackFact>> TripsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        await monitor.EvaluateAsync(cancellationToken);
        var from = Start(window.From);
        var to = Start(window.To.AddDays(1));
        var query = db.Shipments.AsNoTracking().Where(t => t.Execution != ExecutionStatus.Cancelled && ((t.PlannedArrivalAt ?? t.PlannedStartAt ?? t.StartedAt) >= from) && ((t.PlannedArrivalAt ?? t.PlannedStartAt ?? t.StartedAt) < to));
        if (window.TransporterId is { } own)
        {
            query = query.Where(t => t.TransporterId == own);
        }

        var list = await query.ToListAsync(cancellationToken);
        if (list.Count == 0)
        {
            return [];
        }

        var ids = list.Select(t => t.Id).ToList();
        var stops = (await db.Stops.AsNoTracking().Where(s => ids.Contains(s.TrackedShipmentId)).Select(s => s.TrackedShipmentId).ToListAsync(cancellationToken)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var unplanned = (await db.Dwells.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId) && d.Kind == DwellKind.UnplannedStop).Select(d => d.TrackedShipmentId).ToListAsync(cancellationToken)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var dwellMin = (await db.Dwells.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId)).Select(d => new { d.TrackedShipmentId, d.DurationMinutes }).ToListAsync(cancellationToken)).GroupBy(x => x.TrackedShipmentId).ToDictionary(g => g.Key, g => g.Sum(x => x.DurationMinutes));
        var openExc = (await db.Exceptions.AsNoTracking().Where(e => ids.Contains(e.TrackedShipmentId) && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed).Select(e => e.TrackedShipmentId).ToListAsync(cancellationToken)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var deviationKm = (await db.Deviations.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId)).Select(d => new { d.TrackedShipmentId, d.DistanceFromRouteKm }).ToListAsync(cancellationToken)).GroupBy(x => x.TrackedShipmentId).ToDictionary(g => g.Key, g => g.Sum(x => x.DistanceFromRouteKm));
        var routes = (await db.Routes.AsNoTracking().Where(r => ids.Contains(r.TrackedShipmentId)).ToListAsync(cancellationToken)).GroupBy(r => r.TrackedShipmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());
        var names = await NamesAsync(list.Select(t => t.TransporterId), cancellationToken);
        return list.Select(t =>
        {
            var lane = Lane(t);
            var actualMin = t.StartedAt is { } s && (t.CompletedAt ?? t.LastCapturedAt) is { } e ? (int?)(e - s).TotalMinutes : null;
            var execution = t.Execution switch { ExecutionStatus.Completed or ExecutionStatus.Delivered => "Completed", ExecutionStatus.Planned or ExecutionStatus.EnRouteToOrigin or ExecutionStatus.ArrivedOrigin or ExecutionStatus.Loading => "NotStarted", _ => "InTransit" };
            return new TrackFact(t.TripReference, t.ShipmentReference, t.VehicleReference, t.TransporterId, t.TransporterId is { } id ? names.GetValueOrDefault(id) : null, lane,
                t.Tracking switch { TrackingHealth.Healthy => "Healthy", TrackingHealth.Stale => "Stale", TrackingHealth.Lost => "Lost", TrackingHealth.Completed => "Completed", _ => "NotStarted" }, execution,
                t.Risk switch { RiskStatus.OnTime => "OnTime", RiskStatus.AtRisk => "AtRisk", RiskStatus.Delayed => "Delayed", RiskStatus.SeverelyDelayed => "SeverelyDelayed", _ => "OnTime" },
                t.PlannedArrivalAt, t.SystemEtaAt, t.CompletedAt, t.PlannedDistanceKm, t.TravelledKm > 0 ? (decimal)t.TravelledKm : null, t.PlannedDurationMinutes, execution == "Completed" ? actualMin : null, stops.GetValueOrDefault(t.Id),
                execution == "Completed" ? stops.GetValueOrDefault(t.Id) + unplanned.GetValueOrDefault(t.Id) : null, unplanned.GetValueOrDefault(t.Id), dwellMin.GetValueOrDefault(t.Id), (decimal)Math.Round(deviationKm.GetValueOrDefault(t.Id), 1),
                t.LastLatitude, t.LastLongitude, t.LastCapturedAt, openExc.GetValueOrDefault(t.Id), Day(t.PlannedArrivalAt ?? t.PlannedStartAt ?? t.StartedAt ?? DateTimeOffset.UtcNow), Route(routes.GetValueOrDefault(t.Id)?.PointsJson), Route(t.ActualRouteJson));
        }).ToList();
    }

    /// <summary>A stored route as points; routes can be long, so only about 60 are kept for drawing.</summary>
    private static List<TrackPoint>? Route(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var points = new List<TrackPoint>();
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                {
                    points.Add(new TrackPoint(p[0].GetDouble(), p[1].GetDouble()));
                }
                else if (p.ValueKind == JsonValueKind.Object && TryNumber(p, "lat", "latitude", out var lat) && TryNumber(p, "lon", "lng", out var lon, "longitude"))
                {
                    points.Add(new TrackPoint(lat, lon));
                }
            }

            var step = Math.Max(1, points.Count / 60);
            return points.Where((_, i) => i % step == 0).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryNumber(JsonElement e, string a, string b, out double value, string? c = null)
    {
        foreach (var name in new[] { a, b, c })
        {
            if (name is not null && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            {
                value = v.GetDouble();
                return true;
            }
        }

        value = 0;
        return false;
    }

    private async Task<Dictionary<Guid, (Guid? TransporterId, string? Name)>> TripOwnersAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await db.Shipments.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.TransporterId }).ToListAsync(cancellationToken);
        var names = await NamesAsync(rows.Select(r => r.TransporterId), cancellationToken);
        return rows.ToDictionary(r => r.Id, r => (r.TransporterId, r.TransporterId is { } t ? names.GetValueOrDefault(t) : null));
    }

    public async Task<IReadOnlyList<DeviationFact>> DeviationsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = Start(window.From);
        var to = Start(window.To.AddDays(1));
        var list = await db.Deviations.AsNoTracking().Where(d => d.DetectedAt >= from && d.DetectedAt < to).ToListAsync(cancellationToken);
        var owners = await TripOwnersAsync(list.Select(d => d.TrackedShipmentId).Distinct(), cancellationToken);
        var lanes = (await db.Shipments.AsNoTracking().Where(t => list.Select(d => d.TrackedShipmentId).Contains(t.Id)).ToListAsync(cancellationToken)).ToDictionary(t => t.Id, Lane);
        return list.Where(d => window.TransporterId is null || owners.GetValueOrDefault(d.TrackedShipmentId).TransporterId == window.TransporterId)
            .Select(d => new DeviationFact(d.TripReference, d.ShipmentReference, d.VehicleReference, owners.GetValueOrDefault(d.TrackedShipmentId).TransporterId, owners.GetValueOrDefault(d.TrackedShipmentId).Name,
                lanes.GetValueOrDefault(d.TrackedShipmentId, string.Empty), (decimal)Math.Round(d.DistanceFromRouteKm, 1), d.DurationMinutes, d.DetectedAt, d.Reason?.ToString() ?? d.ReasonNote, d.Status == DeviationStatus.Open ? "Open" : "Closed")).ToList();
    }

    public async Task<IReadOnlyList<DwellFact>> DwellsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = Start(window.From);
        var to = Start(window.To.AddDays(1));
        var list = await db.Dwells.AsNoTracking().Where(d => d.StartAt >= from && d.StartAt < to).ToListAsync(cancellationToken);
        var owners = await TripOwnersAsync(list.Select(d => d.TrackedShipmentId).Distinct(), cancellationToken);
        var stopIds = list.Where(d => d.StopId != null).Select(d => d.StopId!.Value).Distinct().ToList();
        var stops = await db.Stops.AsNoTracking().Where(s => stopIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        return list.Where(d => window.TransporterId is null || owners.GetValueOrDefault(d.TrackedShipmentId).TransporterId == window.TransporterId).Select(d =>
        {
            var kind = d.Kind == DwellKind.UnplannedStop ? "Unplanned" : d.StopId is { } sid && stops.TryGetValue(sid, out var stop) ? stop.Kind switch { StopKind.Pickup => "Origin", StopKind.Drop => "Customer", _ => "Hub" } : "Hub";
            return new DwellFact(d.TripReference, d.ShipmentReference, owners.GetValueOrDefault(d.TrackedShipmentId).TransporterId, kind, d.Place ?? "Unnamed place", d.ExpectedDurationMinutes, d.DurationMinutes, d.StartAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<GapFact>> GapsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = Start(window.From);
        var to = Start(window.To.AddDays(1));
        var list = await db.Gaps.AsNoTracking().Where(g => g.GapStart >= from && g.GapStart < to).ToListAsync(cancellationToken);
        var owners = await TripOwnersAsync(list.Select(g => g.TrackedShipmentId).Distinct(), cancellationToken);
        return list.Where(g => window.TransporterId is null || owners.GetValueOrDefault(g.TrackedShipmentId).TransporterId == window.TransporterId)
            .Select(g => new GapFact(g.TripReference, g.ShipmentReference, g.VehicleReference, owners.GetValueOrDefault(g.TrackedShipmentId).TransporterId, owners.GetValueOrDefault(g.TrackedShipmentId).Name,
                $"{g.LastKnownLatitude:0.000}, {g.LastKnownLongitude:0.000}", g.GapStart, g.GapEnd, g.DurationMinutes, g.Severity switch { Severity.Critical => "Critical", Severity.High => "High", Severity.Warning => "Warning", _ => "Info" }, g.GapEnd is null ? "Open" : "Closed")).ToList();
    }

    public async Task<IReadOnlyList<ExceptionFact>> ExceptionsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var query = db.Exceptions.AsNoTracking().AsQueryable();
        if (window.TransporterId is { } own)
        {
            query = query.Where(e => e.TransporterId == own);
        }

        var list = await query.OrderByDescending(e => e.RaisedAt).Take(2_000).ToListAsync(cancellationToken);
        var names = await NamesAsync(list.Select(e => e.TransporterId), cancellationToken);
        var lanes = (await db.Shipments.AsNoTracking().Where(t => list.Select(e => e.TrackedShipmentId).Contains(t.Id)).ToListAsync(cancellationToken)).ToDictionary(t => t.Id, Lane);
        return list.Select(e => new ExceptionFact("Tracking", e.Number, e.Type.ToString(), e.Severity switch { Severity.Critical => "Critical", Severity.High => "High", Severity.Warning => "Warning", _ => "Info" }, e.ShipmentReference, e.TransporterId,
            e.TransporterId is { } t ? names.GetValueOrDefault(t) : null, lanes.GetValueOrDefault(e.TrackedShipmentId), e.RaisedAt, e.ResolvedAt ?? e.ClosedAt, e.Department,
            e.Status switch { ExceptionStatus.Resolved or ExceptionStatus.Closed => "Resolved", ExceptionStatus.Open => "Open", _ => "InProgress" })).ToList();
    }
}
