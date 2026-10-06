using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Application.Queries;

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public sealed record TrackingReportQuery(
    DateOnly? From = null, DateOnly? To = null, Guid? TransporterId = null, string? Vehicle = null, string? Customer = null, string? Lane = null, string? GroupBy = null, string? Format = null, int? LeadHours = null);

/// <summary>The tracking reports, as CSV or Excel. Staff only. Times are shown in Indian time; they are stored in UTC.</summary>
internal sealed class ReportsHandler(TrackingDbContext db, TrackingAccess access, ComplianceHandler compliance, TimeProvider clock)
{
    private const int Cap = 50_000;
    private const string Csv = "text/csv; charset=utf-8";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IReadOnlyList<string> Reports { get; } = ["shipments", "vehicles", "deviations", "dwell", "eta-accuracy", "tracking-health", "delays", "exceptions", "planned-vs-actual", "compliance"];

    public async Task<Result<ReportFile>> RunAsync(string report, TrackingReportQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return TrackingAccess.Forbidden;
        }

        var format = (query.Format ?? "csv").ToLowerInvariant();
        if (format is not ("csv" or "xlsx"))
        {
            return Error.Validation("reports.format_invalid", "Choose csv or xlsx.");
        }

        if (!Reports.Contains(report, StringComparer.OrdinalIgnoreCase))
        {
            return Error.NotFound("reports.not_found", "There is no such report.");
        }

        var today = clock.TodayInIndia();
        var toDay = query.To ?? today;
        var fromDay = query.From ?? toDay.AddDays(-30);
        var since = new DateTimeOffset(fromDay.ToDateTime(TimeOnly.MinValue), Clock.India);
        var until = new DateTimeOffset(toDay.AddDays(1).ToDateTime(TimeOnly.MinValue), Clock.India);
        var rows = await BuildAsync(report.ToLowerInvariant(), query, since, until, cancellationToken);
        return await File(format, $"tracking-{report.ToLowerInvariant()}-{fromDay:yyyyMMdd}-{toDay:yyyyMMdd}", rows);
    }

    private IQueryable<TrackedShipment> Shipments(TrackingReportQuery query, DateTimeOffset since, DateTimeOffset until)
    {
        var rows = db.Shipments.AsNoTracking().Include(s => s.Stops).Where(s => (s.StartedAt ?? s.PlannedStartAt ?? s.CreatedAt) >= since && (s.StartedAt ?? s.PlannedStartAt ?? s.CreatedAt) < until);
        if (query.TransporterId is { } t)
        {
            rows = rows.Where(s => s.TransporterId == t);
        }

        if (!string.IsNullOrWhiteSpace(query.Vehicle))
        {
            var v = query.Vehicle.Trim();
            rows = rows.Where(s => s.VehicleReference != null && s.VehicleReference.Contains(v));
        }

        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var c = query.Customer.Trim();
            rows = rows.Where(s => s.CustomerName != null && s.CustomerName.Contains(c));
        }

        if (!string.IsNullOrWhiteSpace(query.Lane))
        {
            var l = query.Lane.Trim();
            rows = rows.Where(s => (s.OriginName != null && s.OriginName.Contains(l)) || (s.DestinationName != null && s.DestinationName.Contains(l)));
        }

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> BuildAsync(string report, TrackingReportQuery query, DateTimeOffset since, DateTimeOffset until, CancellationToken ct)
    {
        if (report == "compliance")
        {
            return await compliance.ReportAsync(new ComplianceQuery(DateOnly.FromDateTime(since.DateTime), DateOnly.FromDateTime(until.AddDays(-1).DateTime), query.TransporterId, query.GroupBy), ct);
        }

        var shipments = await Shipments(query, since, until).OrderBy(s => s.ShipmentReference).Take(Cap).ToListAsync(ct);
        var ids = shipments.Select(s => s.Id).ToList();
        var byId = shipments.ToDictionary(s => s.Id);
        static ShipmentStop? Final(TrackedShipment s) => s.Stops.Where(x => x.Kind == StopKind.Drop).MaxBy(x => x.Sequence) ?? s.Stops.MaxBy(x => x.Sequence);

        switch (report)
        {
            case "shipments":
                return shipments.Select(s => new Dictionary<string, object?>
                {
                    ["Shipment"] = s.ShipmentReference, ["Trip"] = s.TripReference, ["Transporter"] = s.TransporterReference, ["Vehicle"] = s.VehicleReference, ["Driver"] = s.DriverName, ["Customer"] = s.CustomerName,
                    ["From"] = s.OriginName, ["To"] = s.DestinationName, ["Status"] = s.Execution.ToString(), ["Tracking"] = s.Tracking.ToString(), ["Risk"] = s.Risk.ToString(), ["Planned arrival"] = Time(s.PlannedArrivalAt),
                    ["Expected arrival"] = Time(s.CurrentEtaAt), ["Actual arrival"] = Time(Final(s)?.ArrivedAt), ["Planned km"] = s.PlannedDistanceKm, ["Actual km"] = Math.Round(s.TravelledKm, 1),
                    ["Progress %"] = s.ProgressPct is { } p ? Math.Round(p, 0) : null,
                }).ToList();

            case "vehicles":
                return shipments.Where(s => s.VehicleReference != null).GroupBy(s => s.VehicleReference!).OrderBy(g => g.Key).Select(g => new Dictionary<string, object?>
                {
                    ["Vehicle"] = g.Key, ["Trips"] = g.Count(), ["Km travelled"] = Math.Round(g.Sum(s => s.TravelledKm), 1), ["Transporter"] = g.First().TransporterReference,
                    ["Last location"] = Time(g.Max(s => s.LastCapturedAt)), ["Tracking now"] = g.OrderByDescending(s => s.LastCapturedAt).First().Tracking.ToString(),
                }).ToList();

            case "deviations":
                return (await db.Deviations.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId)).OrderBy(d => d.DetectedAt).Take(Cap).ToListAsync(ct)).Select(d => new Dictionary<string, object?>
                {
                    ["Shipment"] = d.ShipmentReference, ["Trip"] = d.TripReference, ["Vehicle"] = d.VehicleReference, ["Transporter"] = byId.GetValueOrDefault(d.TrackedShipmentId)?.TransporterReference, ["Detected"] = Time(d.DetectedAt),
                    ["Km from route"] = d.DistanceFromRouteKm, ["Minutes"] = d.DurationMinutes, ["Severity"] = d.Severity.ToString(), ["Status"] = d.Status.ToString(), ["Resolved"] = Time(d.ResolvedAt),
                    ["Reason"] = d.Reason?.ToString(), ["Note"] = d.ReasonNote,
                }).ToList();

            case "dwell":
                return (await db.Dwells.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId)).OrderBy(d => d.StartAt).Take(Cap).ToListAsync(ct)).Select(d => new Dictionary<string, object?>
                {
                    ["Shipment"] = d.ShipmentReference, ["Vehicle"] = d.VehicleReference, ["Transporter"] = byId.GetValueOrDefault(d.TrackedShipmentId)?.TransporterReference, ["Place"] = d.Place, ["Kind"] = d.Kind.ToString(),
                    ["From"] = Time(d.StartAt), ["To"] = Time(d.EndAt), ["Minutes"] = d.DurationMinutes, ["Expected minutes"] = d.ExpectedDurationMinutes, ["Excess minutes"] = d.ExcessDurationMinutes, ["Status"] = d.Status.ToString(),
                }).ToList();

            case "eta-accuracy":
                return await EtaAccuracyAsync(shipments, query, ct);

            case "tracking-health":
            {
                var gaps = (await db.Gaps.AsNoTracking().Where(g => ids.Contains(g.TrackedShipmentId)).ToListAsync(ct)).ToLookup(g => g.TrackedShipmentId);
                var counts = await db.Locations.AsNoTracking().Where(l => db.Shipments.Any(s => s.ShipmentId == l.ShipmentId && ids.Contains(s.Id))).GroupBy(l => l.ShipmentId)
                    .Select(g => new { g.Key, Total = g.Count(), Suspicious = g.Count(l => l.Validation == LocationValidation.Suspicious) }).ToDictionaryAsync(g => g.Key, ct);
                return shipments.Where(s => s.StartedAt is not null).Select(s =>
                {
                    var end = s.CompletedAt ?? s.LastCapturedAt ?? clock.GetUtcNow();
                    var expected = Math.Max(1, (int)(end - s.StartedAt!.Value).TotalMinutes);
                    var gapMinutes = gaps[s.Id].Sum(g => g.DurationMinutes);
                    counts.TryGetValue(s.ShipmentId, out var c);
                    return new Dictionary<string, object?>
                    {
                        ["Shipment"] = s.ShipmentReference, ["Vehicle"] = s.VehicleReference, ["Transporter"] = s.TransporterReference, ["Driver"] = s.DriverName, ["Tracked from"] = Time(s.StartedAt), ["Tracked to"] = Time(end),
                        ["Expected minutes"] = expected, ["Minutes without a location"] = gapMinutes, ["Coverage %"] = Math.Round(Math.Clamp((expected - gapMinutes) * 100.0 / expected, 0, 100), 1), ["Gaps"] = gaps[s.Id].Count(),
                        ["Locations"] = c?.Total ?? 0, ["Suspicious locations"] = c?.Suspicious ?? 0, ["Tracking now"] = s.Tracking.ToString(),
                    };
                }).ToList();
            }

            case "delays":
                return shipments.Select(s => (Shipment: s, Stop: Final(s))).Where(x => x.Stop is not null && (x.Stop.WindowEnd ?? x.Stop.PlannedArrival) is not null).Select(x =>
                {
                    var planned = (x.Stop!.WindowEnd ?? x.Stop.PlannedArrival)!.Value;
                    var actual = x.Stop.ArrivedAt;
                    var delay = actual is { } a ? (int)(a - planned).TotalMinutes : x.Shipment.DelayMinutes;
                    return new Dictionary<string, object?>
                    {
                        ["Shipment"] = x.Shipment.ShipmentReference, ["Transporter"] = x.Shipment.TransporterReference, ["Vehicle"] = x.Shipment.VehicleReference, ["Customer"] = x.Shipment.CustomerName, ["To"] = x.Shipment.DestinationName,
                        ["Planned"] = Time(planned), ["Actual arrival"] = Time(actual), ["Expected arrival"] = Time(x.Shipment.CurrentEtaAt), ["Delay minutes"] = delay, ["Risk"] = x.Shipment.Risk.ToString(),
                        ["Delay reason"] = x.Shipment.DelayReason?.ToString(), ["Note"] = x.Shipment.DelayNote,
                    };
                }).Where(r => (int)r["Delay minutes"]! > 0).OrderByDescending(r => (int)r["Delay minutes"]!).ToList();

            case "exceptions":
                return (await db.Exceptions.AsNoTracking().Where(e => ids.Contains(e.TrackedShipmentId)).OrderBy(e => e.RaisedAt).Take(Cap).ToListAsync(ct)).Select(e => new Dictionary<string, object?>
                {
                    ["Exception"] = e.Number, ["Type"] = e.Type.ToString(), ["Severity"] = e.Severity.ToString(), ["Status"] = e.Status.ToString(), ["Shipment"] = e.ShipmentReference, ["Vehicle"] = e.VehicleReference,
                    ["Transporter"] = e.TransporterReference, ["Raised"] = Time(e.RaisedAt), ["Due"] = Time(e.DueAt), ["Resolved"] = Time(e.ResolvedAt), ["Escalation level"] = e.EscalationLevel, ["Escalated to"] = e.EscalatedTo,
                    ["Cause"] = e.RootCause, ["Delay reason"] = e.DelayReason?.ToString(), ["Action taken"] = e.ActionTaken, ["Description"] = e.Description,
                }).ToList();

            default: // planned-vs-actual
            {
                var dwells = (await db.Dwells.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId)).ToListAsync(ct)).ToLookup(d => d.TrackedShipmentId);
                var deviations = (await db.Deviations.AsNoTracking().Where(d => ids.Contains(d.TrackedShipmentId)).ToListAsync(ct)).ToLookup(d => d.TrackedShipmentId);
                return shipments.Where(s => s.StartedAt is not null).Select(s =>
                {
                    var end = s.CompletedAt ?? s.LastCapturedAt;
                    int? actualMinutes = end is { } e ? (int)(e - s.StartedAt!.Value).TotalMinutes : null;
                    return new Dictionary<string, object?>
                    {
                        ["Shipment"] = s.ShipmentReference, ["Vehicle"] = s.VehicleReference, ["Transporter"] = s.TransporterReference, ["Planned km"] = s.PlannedDistanceKm, ["Actual km"] = Math.Round(s.TravelledKm, 1),
                        ["Km variance"] = s.PlannedDistanceKm is { } p ? Math.Round(s.TravelledKm - (double)p, 1) : null, ["Planned minutes"] = s.PlannedDurationMinutes, ["Actual minutes"] = actualMinutes,
                        ["Minutes variance"] = s.PlannedDurationMinutes is { } pm && actualMinutes is { } am ? am - pm : null, ["Planned stops"] = s.Stops.Count,
                        ["Stops reached"] = s.Stops.Count(x => x.Status is StopStatus.Arrived or StopStatus.Departed), ["Unplanned stops"] = dwells[s.Id].Count(d => d.Kind == DwellKind.UnplannedStop),
                        ["Dwell minutes"] = dwells[s.Id].Sum(d => d.DurationMinutes), ["Deviation minutes"] = deviations[s.Id].Sum(d => d.DurationMinutes),
                    };
                }).ToList();
            }
        }
    }

    /// <summary>
    /// How good the arrival estimates were. For each finished trip, the estimate made closest to a chosen number of hours before it arrived is compared with when it did. "Right" means
    /// within 15 minutes. Broken down by lane, transporter, customer or hour of arrival.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> EtaAccuracyAsync(List<TrackedShipment> shipments, TrackingReportQuery query, CancellationToken cancellationToken)
    {
        var lead = Math.Clamp(query.LeadHours ?? 2, 0, 48);
        var finished = shipments.Select(s => (Shipment: s, Stop: s.Stops.Where(x => x.Kind == StopKind.Drop).MaxBy(x => x.Sequence) ?? s.Stops.MaxBy(x => x.Sequence))).Where(x => x.Stop?.ArrivedAt is not null).ToList();
        var ids = finished.Select(x => x.Shipment.Id).ToList();
        var predictions = (await db.EtaPredictions.AsNoTracking().Where(p => ids.Contains(p.TrackedShipmentId) && p.IsFinalDestination).ToListAsync(cancellationToken)).ToLookup(p => p.TrackedShipmentId);
        var rows = new List<(TrackedShipment S, DateTimeOffset Actual, EtaPrediction P, double Error)>();
        foreach (var (shipment, stop) in finished)
        {
            var actual = stop!.ArrivedAt!.Value;
            var target = actual.AddHours(-lead);
            var prediction = predictions[shipment.Id].Where(p => p.PredictedAt <= actual).OrderBy(p => Math.Abs((p.PredictedAt - target).TotalMinutes)).FirstOrDefault();
            if (prediction is not null)
            {
                rows.Add((shipment, actual, prediction, (prediction.PredictedEta - actual).TotalMinutes));
            }
        }

        var group = (query.GroupBy ?? "none").ToLowerInvariant();
        string Key((TrackedShipment S, DateTimeOffset Actual, EtaPrediction P, double Error) r) => group switch
        {
            "lane" => $"{r.S.OriginName} → {r.S.DestinationName}",
            "transporter" => r.S.TransporterReference ?? "Unknown",
            "customer" => r.S.CustomerName ?? "Unknown",
            "hour" => $"{r.Actual.ToOffset(Clock.India).Hour:00}:00",
            _ => "All trips",
        };

        var summary = rows.GroupBy(Key).OrderBy(g => g.Key).Select(g =>
        {
            var errors = g.Select(r => Math.Abs(r.Error)).Order().ToList();
            var median = errors.Count % 2 == 1 ? errors[errors.Count / 2] : (errors[errors.Count / 2 - 1] + errors[errors.Count / 2]) / 2;
            return new Dictionary<string, object?>
            {
                ["Group"] = g.Key, ["Trips"] = g.Count(), ["Average error (min)"] = Math.Round(errors.Average(), 1), ["Median error (min)"] = Math.Round(median, 1),
                ["Within 15 min %"] = Math.Round(errors.Count(e => e <= 15) * 100.0 / errors.Count, 1), ["Estimated hours before arrival"] = lead,
            };
        }).ToList();

        // The trips behind the figures follow, so a number can be traced to what produced it.
        summary.AddRange(rows.OrderBy(r => r.S.ShipmentReference).Select(r => new Dictionary<string, object?>
        {
            ["Group"] = $"  {r.S.ShipmentReference}", ["Trips"] = null, ["Average error (min)"] = Math.Round(r.Error, 1), ["Median error (min)"] = null, ["Within 15 min %"] = null, ["Estimated hours before arrival"] = lead,
        }));
        return summary;
    }

    private static string? Time(DateTimeOffset? t) => t?.ToOffset(Clock.India).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A leading =, +, - or @ makes Excel evaluate a cell; a leading apostrophe keeps it as text.</summary>
    internal static string Safe(string value) => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static async Task<ReportFile> File(string format, string name, List<Dictionary<string, object?>> rows)
    {
        if (format == "xlsx")
        {
            using var stream = new MemoryStream();
            await MiniExcel.SaveAsAsync(stream, rows.Select(r => r.ToDictionary(kv => kv.Key, kv => kv.Value is string s ? Safe(s) : kv.Value)).ToList());
            return new ReportFile(stream.ToArray(), Xlsx, $"{name}.xlsx");
        }

        var sb = new StringBuilder();
        if (rows.Count > 0)
        {
            var columns = rows[0].Keys.ToList();
            sb.AppendJoin(',', columns.Select(Quote)).Append("\r\n");
            foreach (var row in rows)
            {
                sb.AppendJoin(',', columns.Select(c => Quote(row.GetValueOrDefault(c) switch { null => string.Empty, string s => Safe(s), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), var v => v.ToString() ?? string.Empty }))).Append("\r\n");
            }
        }

        return new ReportFile(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString()), Csv, $"{name}.csv");
    }

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
