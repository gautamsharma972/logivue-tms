using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Application.Queries;

public sealed record ComplianceQuery(DateOnly? From = null, DateOnly? To = null, Guid? TransporterId = null, string? GroupBy = null);

public sealed record ComplianceDto(string GroupBy, DateOnly From, DateOnly To, ComplianceRow Overall, IReadOnlyList<ComplianceRow> Rows, ComplianceSetting Rules, string Note);

/// <summary>How well tracking worked and how drivers used it: coverage, gaps, starting on time, keeping it on, stopping properly. Staff only, and descriptive: it is not a transporter score.</summary>
internal sealed class ComplianceHandler(TrackingDbContext db, TrackingAccess access, ITrackingSettings settings, TimeProvider clock)
{
    public const string Note = "Operational figures only. They do not change a transporter's score unless a rule is configured for that; a blank rate means nothing could be measured.";

    public async Task<Result<ComplianceDto>> GetAsync(ComplianceQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return TrackingAccess.Forbidden;
        }

        var groupBy = (query.GroupBy ?? "transporter").ToLowerInvariant();
        if (groupBy is not ("transporter" or "driver" or "vehicle"))
        {
            return Error.Validation("compliance.group_invalid", "Group by transporter, driver or vehicle.");
        }

        var to = query.To ?? clock.TodayInIndia();
        var from = query.From ?? to.AddDays(-30);
        var rows = await RowsAsync(from, to, query.TransporterId, groupBy, cancellationToken);
        var rules = await settings.GetAsync<ComplianceSetting>(TrackingSettingKeys.Compliance, cancellationToken);
        var health = await settings.GetAsync<HealthSetting>(TrackingSettingKeys.Health, cancellationToken);
        var all = rows.SelectMany(r => r.Trips).ToList();
        var overall = ComplianceCalculator.Row("all", "All trips", all, rules, health);
        var grouped = rows.GroupBy(r => (r.Key, r.Name)).Select(g => ComplianceCalculator.Row(g.Key.Key, g.Key.Name, g.SelectMany(x => x.Trips).ToList(), rules, health)).OrderBy(r => r.Name).ToList();
        return new ComplianceDto(groupBy, from, to, overall, grouped, rules, Note);
    }

    private sealed record Keyed(string Key, string Name, IReadOnlyList<TripTracking> Trips);

    private async Task<List<Keyed>> RowsAsync(DateOnly from, DateOnly to, Guid? transporterId, string groupBy, CancellationToken cancellationToken)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Clock.India);
        var until = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Clock.India);
        var now = clock.GetUtcNow();
        var trips = db.Shipments.AsNoTracking().Where(s => s.StartedAt != null && s.StartedAt >= since && s.StartedAt < until);
        if (transporterId is { } t)
        {
            trips = trips.Where(s => s.TransporterId == t);
        }

        var list = await trips.Take(50_000).ToListAsync(cancellationToken);
        var ids = list.Select(s => s.Id).ToList();
        var gaps = (await db.Gaps.AsNoTracking().Where(g => ids.Contains(g.TrackedShipmentId)).Select(g => new { g.TrackedShipmentId, g.DurationMinutes, g.GapStart, g.GapEnd }).ToListAsync(cancellationToken)).ToLookup(g => g.TrackedShipmentId);
        var sessions = (await db.Sessions.AsNoTracking().Where(x => ids.Contains(x.TrackedShipmentId)).Select(x => new { x.TrackedShipmentId, x.StartedAt, x.Status, x.DriverReference }).ToListAsync(cancellationToken)).ToLookup(x => x.TrackedShipmentId);

        return list.Select(s =>
        {
            var mine = sessions[s.Id].OrderBy(x => x.StartedAt).ToList();
            var last = mine.LastOrDefault();
            var end = s.CompletedAt ?? s.LastCapturedAt ?? now;
            var minutes = (int)Math.Max(1, (end - s.StartedAt!.Value).TotalMinutes);
            bool? proper = last is null || last.Status is TrackingSessionStatus.Active or TrackingSessionStatus.Paused or TrackingSessionStatus.Stale or TrackingSessionStatus.Lost ? null : last.Status == TrackingSessionStatus.Completed;
            // An open gap counts up to now (or the trip's end), so a vehicle that is still silent is not credited with coverage it does not have.
            var gapMinutes = gaps[s.Id].Select(g => g.GapEnd is null ? Math.Max(g.DurationMinutes, (int)(end - g.GapStart).TotalMinutes) : g.DurationMinutes).ToList();
            var (key, name) = groupBy switch
            {
                "driver" => (last?.DriverReference ?? s.DriverName ?? "Unknown driver", last?.DriverReference ?? s.DriverName ?? "Unknown driver"),
                "vehicle" => (s.VehicleReference ?? "No vehicle", s.VehicleReference ?? "No vehicle"),
                _ => (s.TransporterId?.ToString() ?? "none", s.TransporterReference ?? "No carrier"),
            };
            return new Keyed(key, name, [new TripTracking(key, name, s.PlannedStartAt, mine.FirstOrDefault()?.StartedAt ?? s.StartedAt, minutes, gapMinutes, proper)]);
        }).ToList();
    }

    /// <summary>The same rows as flat dictionaries, for the CSV/Excel report.</summary>
    public async Task<List<Dictionary<string, object?>>> ReportAsync(ComplianceQuery query, CancellationToken cancellationToken)
    {
        var result = await GetAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return [];
        }

        return result.Value.Rows.Select(r => new Dictionary<string, object?>
        {
            [result.Value.GroupBy switch { "driver" => "Driver", "vehicle" => "Vehicle", _ => "Transporter" }] = r.Name, ["Trips"] = r.Trips, ["Expected minutes"] = r.ExpectedMinutes, ["Tracked minutes"] = r.ActualMinutes,
            ["Coverage %"] = r.CoveragePct, ["Gaps"] = r.Gaps, ["Trips with a stale gap"] = r.StaleTrips, ["Trips with a lost gap"] = r.LostTrips, ["Started on time %"] = r.StartedOnTime, ["Kept tracking active %"] = r.KeptActive,
            ["Stopped properly %"] = r.StoppedProperly, ["Trips with repeated gaps"] = r.TripsWithRepeatedGaps,
        }).ToList();
    }
}
