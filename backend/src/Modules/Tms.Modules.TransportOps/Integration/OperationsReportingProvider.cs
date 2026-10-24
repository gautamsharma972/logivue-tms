using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.TransportOps.Integration;

/// <summary>What the Reports module reads from this module: flat facts, one row per LR and per invoice. This module stays the source of truth; nothing here can be written back.</summary>
internal sealed class OperationsReportingProvider(TransportOpsDbContext db) : IOperationsReportingProvider
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public async Task<IReadOnlyList<OpsLrFact>> LrsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = new DateTimeOffset(window.From.ToDateTime(TimeOnly.MinValue), Ist);
        var to = new DateTimeOffset(window.To.AddDays(1).ToDateTime(TimeOnly.MinValue), Ist);
        // An LR belongs to the period of its delivery date, or of its creation when it has none; open work is always included so pending POD and holds are never cut off by a date.
        var lrs = await db.Lrs.AsNoTracking().Include(l => l.Lines)
            .Where(l => (l.DeliveryDate != null ? l.DeliveryDate >= window.From && l.DeliveryDate <= window.To : l.CreatedAt >= from && l.CreatedAt < to) || l.Status == DeliveryStatus.InTransit || l.Status == DeliveryStatus.Arrived)
            .ToListAsync(cancellationToken);
        var ids = lrs.Select(l => l.Id).ToList();
        var customerIds = lrs.Select(l => l.CustomerId).Distinct().ToList();
        var placeIds = lrs.SelectMany(l => new[] { l.CfaLocationId, l.DestinationLocationId }).Distinct().ToList();
        var requestIds = lrs.Where(l => l.RequestId != null).Select(l => l.RequestId!.Value).Distinct().ToList();
        var tripIds = lrs.Where(l => l.TripId != null).Select(l => l.TripId!.Value).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var places = await db.Locations.AsNoTracking().Where(p => placeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var requests = await db.Requests.AsNoTracking().Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var trips = await db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, cancellationToken);
        var pods = await db.Pods.AsNoTracking().Include(p => p.Documents).Include(p => p.Reviews).Where(p => ids.Contains(p.LrId)).ToDictionaryAsync(p => p.LrId, cancellationToken);
        var calcs = (await db.Calculations.AsNoTracking().Where(c => ids.Contains(c.LrId) && c.Status != CalculationStatus.Superseded).ToListAsync(cancellationToken)).ToDictionary(c => c.LrId);
        var invoiceIds = calcs.Values.Where(c => c.InvoiceId != null).Select(c => c.InvoiceId!.Value).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id) && i.Status != InvoiceStatus.Cancelled).ToDictionaryAsync(i => i.Id, i => i.Number, cancellationToken);

        return lrs.Select(l =>
        {
            pods.TryGetValue(l.Id, out var pod);
            calcs.TryGetValue(l.Id, out var calc);
            requests.TryGetValue(l.RequestId ?? Guid.Empty, out var request);
            places.TryGetValue(l.CfaLocationId, out var cfa);
            places.TryGetValue(l.DestinationLocationId, out var destination);
            var detentionStatus = calc?.Detention?.Status.ToString();
            return new OpsLrFact(l.Number, l.Id, customers.GetValueOrDefault(l.CustomerId, "?"), l.CustomerReference, request?.Number, request?.ReceivedAt, cfa?.Name ?? "?", destination?.Name ?? "?", destination?.City ?? "?",
                l.VehicleRegistration, l.TripId is { } t ? trips.GetValueOrDefault(t) : null, l.DeliveryDate ?? Day(l.CreatedAt), l.Status.ToString(), l.TotalWeightKg, l.TotalPackages, l.CreatedAt, l.CfaReachedAt, l.LoadingStartedAt,
                l.LoadingCompletedAt, l.DepartedAt, l.ArrivedAt, l.UnloadedAt, (pod?.Status ?? PodStatus.NotYetDue).ToString(), pod?.ReceivedAt, pod?.ReviewedAt, pod?.Reviews.Count(r => r.Action == "Reject") ?? 0,
                pod?.Documents.OrderBy(d => d.UploadedAt).FirstOrDefault()?.Source.ToString(), calc?.Status.ToString(), calc?.BasicFreight, calc?.OtherCharges, calc is null ? null : calc.EffectiveDetention, detentionStatus,
                calc is { Holds.Count: > 0 } ? string.Join(" ", calc.Holds) : null, calc?.InvoiceId is { } i && invoices.TryGetValue(i, out var n) ? n : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<OpsInvoiceFact>> InvoicesAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var rows = await db.Invoices.AsNoTracking().Include(i => i.Submissions)
            .Where(i => i.Status != InvoiceStatus.Draft && ((i.InvoiceDate >= window.From && i.InvoiceDate <= window.To) || (i.Status == InvoiceStatus.Issued && i.AmountPaid < i.GrandTotal)))
            .ToListAsync(cancellationToken);
        var lineCounts = await db.InvoiceLines.AsNoTracking().GroupBy(l => l.InvoiceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return rows.Select(i => new OpsInvoiceFact(i.Number, i.Id, i.CustomerName, i.InvoiceDate, i.DueDate, i.Status.ToString(), i.Submission.ToString(), i.Subtotal, i.Cgst + i.Sgst + i.Igst, i.GrandTotal, i.AmountPaid,
            lineCounts.GetValueOrDefault(i.Id), i.Submissions.Count, i.Submissions.OrderByDescending(s => s.Attempt).FirstOrDefault(s => s.RejectionReason != null)?.RejectionReason, i.Submissions.Count == 0 ? null : i.Submissions.Max(s => s.SubmittedAt))).ToList();
    }

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(Ist).DateTime);
}
