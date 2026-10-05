using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Application.Dashboard;

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

/// <summary>The reports the specification lists, as CSV or Excel. Staff only; each carries the filters it was run with so a printout can be trusted.</summary>
internal sealed class ReportsHandler(DeliveriesDbContext db, DeliveryAccess access, ProofRowSource source, AgeingService ageing, DashboardHandler dashboard, IDeliverySettings settings, TimeProvider clock)
{
    private const int Cap = 50_000;
    private const string Csv = "text/csv; charset=utf-8";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IReadOnlyList<string> Reports { get; } = ["deliveries", "pods", "ageing", "shortages", "damages", "failed", "exceptions", "compliance", "performance"];

    public async Task<Result<ReportFile>> RunAsync(string report, ReportQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return DeliveryAccess.Forbidden;
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

        var (since, to, fromDay, toDay) = ProofRowSource.Range(query.From, query.To, clock);
        var rows = await BuildAsync(report.ToLowerInvariant(), query, since, to, cancellationToken);
        var name = $"{report.ToLowerInvariant()}-{fromDay:yyyyMMdd}-{toDay:yyyyMMdd}";
        return await File(format, name, rows);
    }

    private async Task<List<Dictionary<string, object?>>> BuildAsync(string report, ReportQuery query, DateTimeOffset since, DateTimeOffset to, CancellationToken ct)
    {
        switch (report)
        {
            case "deliveries":
            {
                var d = await Deliveries(query, since, to).OrderBy(x => x.PlannedDeliveryAt).Take(Cap).ToListAsync(ct);
                return d.Select(x => new Dictionary<string, object?>
                {
                    ["Delivery"] = x.Number, ["Shipment"] = x.ShipmentReference, ["Customer"] = x.CustomerName, ["Transporter"] = x.TransporterReference, ["Vehicle"] = x.VehicleReference, ["Planned"] = Time(x.PlannedDeliveryAt),
                    ["Arrived"] = Time(x.ActualArrivalAt), ["Delivered"] = Time(x.ActualDeliveryAt), ["Status"] = x.Status.ToString(), ["Outcome"] = x.Outcome?.ToString(),
                    ["On time"] = x.OnTime is { } on ? (on ? "Yes" : "No") : "Not applicable", ["Quantity mismatch"] = x.HasQuantityMismatch ? "Yes" : "No",
                }).ToList();
            }

            case "pods":
            {
                var pods = await (from p in db.Pods.AsNoTracking().Where(p => p.IsCurrent)
                                  join d in Deliveries(query, since, to) on p.DeliveryId equals d.Id
                                  select new { p, d.Number, d.CustomerName, d.TransporterReference, d.ActualDeliveryAt }).Take(Cap).ToListAsync(ct);
                return pods.Select(x => new Dictionary<string, object?>
                {
                    ["Proof"] = x.p.PodNumber, ["Version"] = x.p.PodVersion, ["Delivery"] = x.Number, ["Customer"] = x.CustomerName, ["Transporter"] = x.TransporterReference, ["Status"] = x.p.Status.ToString(),
                    ["Method"] = x.p.Method?.ToString(), ["Recipient"] = x.p.RecipientName, ["Delivered"] = Time(x.ActualDeliveryAt), ["Submitted"] = Time(x.p.FirstSubmittedAt), ["Accepted"] = Time(x.p.ApprovedAt),
                    ["Times sent back"] = x.p.RejectionCount, ["Accepted automatically"] = x.p.AutoAccepted ? "Yes" : "No",
                }).ToList();
            }

            case "ageing":
            {
                var aged = await ageing.AgedAsync(query.TransporterId, ct);
                var labels = Ageing.Labels(await settings.GetAsync<AgeingSetting>(DeliverySettingKeys.Ageing, ct));
                return aged.OrderByDescending(a => a.Hours).Select(a => new Dictionary<string, object?>
                {
                    ["Waiting for"] = DashboardHandler.StageLabel(a.Stage), ["Delivery"] = a.Row.Number, ["Customer"] = a.Row.Customer, ["Transporter"] = a.Row.Transporter, ["Destination"] = a.Row.Destination,
                    ["Hours waiting"] = Math.Round(a.Hours, 1), ["Age"] = labels[a.Bucket], ["Target (hours)"] = a.Target, ["Overdue"] = a.Overdue ? "Yes" : "No",
                }).ToList();
            }

            case "shortages":
            case "damages":
            {
                var type = report == "shortages" ? DiscrepancyType.Shortage : DiscrepancyType.Damage;
                var found = await (from x in db.Discrepancies.AsNoTracking().Where(x => x.Type == type)
                                   join d in Deliveries(query, since, to) on x.DeliveryId equals d.Id
                                   join i in db.Items.AsNoTracking() on x.DeliveryItemId equals i.Id
                                   select new { d.Number, d.CustomerName, d.TransporterReference, d.ShipmentReference, i.SkuReference, i.DispatchedQuantity, x.Quantity, x.ReasonCode, x.Description, x.CustomerAcknowledged, x.ClaimReference, x.CreatedAt })
                    .OrderBy(x => x.CreatedAt).Take(Cap).ToListAsync(ct);
                return found.Select(x => new Dictionary<string, object?>
                {
                    ["Delivery"] = x.Number, ["Shipment"] = x.ShipmentReference, ["Customer"] = x.CustomerName, ["Transporter"] = x.TransporterReference, ["SKU"] = x.SkuReference, ["Dispatched"] = x.DispatchedQuantity,
                    [report == "shortages" ? "Short" : "Damaged"] = x.Quantity, ["Reason"] = x.ReasonCode, ["Detail"] = x.Description, ["Customer acknowledged"] = x.CustomerAcknowledged ? "Yes" : "No",
                    ["Claim"] = x.ClaimReference, ["Recorded"] = Time(x.CreatedAt),
                }).ToList();
            }

            case "failed":
            {
                var d = await Deliveries(query, since, to).Where(x => x.Status == DeliveryStatus.Failed || x.Status == DeliveryStatus.Refused || x.Attempts.Any(a => a.Result == AttemptResult.Failed))
                    .Include(x => x.Attempts).OrderBy(x => x.PlannedDeliveryAt).Take(Cap).ToListAsync(ct);
                return d.SelectMany(x => x.Attempts.Where(a => a.Result == AttemptResult.Failed).DefaultIfEmpty(), (x, a) => new Dictionary<string, object?>
                {
                    ["Delivery"] = x.Number, ["Customer"] = x.CustomerName, ["Transporter"] = x.TransporterReference, ["Status"] = x.Status.ToString(), ["Attempt"] = a?.AttemptNumber, ["When"] = Time(a?.AttemptedAt),
                    ["Reason"] = a?.ReasonCode, ["Driver remarks"] = a?.DriverRemarks,
                }).ToList();
            }

            case "exceptions":
            {
                var e = db.Exceptions.AsNoTracking().Where(x => x.RaisedAt >= since && x.RaisedAt < to);
                if (source.Scope(query.TransporterId) is { } t)
                {
                    e = e.Where(x => x.TransporterId == t);
                }

                var now = clock.GetUtcNow();
                return (await e.OrderBy(x => x.RaisedAt).Take(Cap).ToListAsync(ct)).Select(x => new Dictionary<string, object?>
                {
                    ["Exception"] = x.Number, ["Delivery"] = x.DeliveryNumber, ["Type"] = x.ExceptionType.ToString(), ["Severity"] = x.Severity.ToString(), ["Status"] = x.Status.ToString(), ["Owner"] = x.Department,
                    ["Raised"] = Time(x.RaisedAt), ["Due"] = Time(x.DueAt), ["Overdue"] = x.IsOpen && x.DueAt < now ? "Yes" : "No", ["Responsible"] = x.ResponsibleParty == ResponsibleParty.Unknown ? "Not established" : x.ResponsibleParty.ToString(),
                    ["Root cause"] = x.RootCause, ["Resolution"] = x.Resolution, ["Financial impact"] = x.FinancialImpact, ["Claim"] = x.ClaimReference, ["Resolved"] = Time(x.ResolvedAt),
                }).ToList();
            }

            default: // compliance, performance
            {
                var result = (await dashboard.ComplianceAsync(new ComplianceQuery(DateOnly.FromDateTime(since.ToOffset(Clock.India).DateTime), DateOnly.FromDateTime(to.AddDays(-1).ToOffset(Clock.India).DateTime), query.GroupBy, query.TransporterId, query.Customer, query.Lane, query.Vehicle, query.ServiceType), ct)).Value;
                return result.Rows.Select(r => new Dictionary<string, object?>
                {
                    [char.ToUpperInvariant(result.GroupBy[0]) + result.GroupBy[1..]] = r.Name, ["Delivered"] = r.Metrics.Delivered, ["Proof received"] = r.Metrics.PodSubmitted, ["Proof pending"] = r.Metrics.PodPending,
                    ["Proof rejected"] = r.Metrics.PodRejected, ["Proof accepted"] = r.Metrics.PodAccepted, ["Submitted within target %"] = Pct(r.Metrics.SubmissionCompliance), ["Accepted %"] = Pct(r.Metrics.AcceptanceRate),
                    ["Rejected %"] = Pct(r.Metrics.RejectionRate), ["On time %"] = Pct(r.Metrics.OnTimeRate), ["Average hours to submit"] = r.Metrics.AverageSubmissionHours,
                    ["Average hours to review"] = r.Metrics.AverageReviewHours, ["Average hours to correct"] = r.Metrics.AverageResubmissionHours,
                }).ToList();
            }
        }
    }

    private IQueryable<Delivery> Deliveries(ReportQuery query, DateTimeOffset since, DateTimeOffset to)
    {
        var rows = db.Deliveries.AsNoTracking().Where(d => (d.ActualDeliveryAt ?? d.PlannedDeliveryAt) >= since && (d.ActualDeliveryAt ?? d.PlannedDeliveryAt) < to);
        if (query.TransporterId is { } t)
        {
            rows = rows.Where(d => d.TransporterId == t);
        }

        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var customer = query.Customer.Trim();
            rows = rows.Where(d => d.CustomerName.Contains(customer));
        }

        if (!string.IsNullOrWhiteSpace(query.Vehicle))
        {
            var vehicle = query.Vehicle.Trim();
            rows = rows.Where(d => d.VehicleReference != null && d.VehicleReference.Contains(vehicle));
        }

        if (!string.IsNullOrWhiteSpace(query.ServiceType))
        {
            var service = query.ServiceType.Trim();
            rows = rows.Where(d => d.ServiceType == service);
        }

        if (!string.IsNullOrWhiteSpace(query.Lane))
        {
            var lane = query.Lane.Trim();
            rows = rows.Where(d => (d.OriginReference != null && d.OriginReference.Contains(lane)) || (d.DestinationReference != null && d.DestinationReference.Contains(lane)));
        }

        return rows;
    }

    private static decimal? Pct(decimal? fraction) => fraction is { } f ? Math.Round(f * 100, 1) : null;

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

        return new ReportFile(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString()), Csv, $"{name}.csv"); // BOM so Excel reads accents
    }

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
