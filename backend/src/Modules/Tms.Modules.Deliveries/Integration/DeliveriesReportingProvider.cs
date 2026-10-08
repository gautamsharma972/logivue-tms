using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Deliveries.Integration;

/// <summary>
/// What Reports &amp; Analytics reads from deliveries and proofs. Statuses, the on-time judgement and the SLA test are this module's own (the same rule that raises
/// <c>PodAccepted</c>); Reports counts and lists them. Read-only.
/// </summary>
internal sealed class DeliveriesReportingProvider(DeliveriesDbContext db, IDeliverySettings settings, ITransporterDirectory transporters) : IPodReportingProvider
{
    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    private static DateTimeOffset Start(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));

    private IQueryable<Delivery> InWindow(ReportingWindow w)
    {
        var from = Start(w.From);
        var to = Start(w.To.AddDays(1));
        var query = db.Deliveries.AsNoTracking().Where(d => d.Status != DeliveryStatus.Cancelled && ((d.ActualDeliveryAt ?? d.PlannedDeliveryAt) >= from) && ((d.ActualDeliveryAt ?? d.PlannedDeliveryAt) < to));
        return w.TransporterId is { } own ? query.Where(d => d.TransporterId == own) : query;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken) =>
        (await transporters.GetAsync(ids.Where(i => i is not null).Select(i => i!.Value).Distinct(), cancellationToken)).ToDictionary(t => t.Key, t => t.Value.LegalName);

    private static string StatusOf(Delivery d) => d.Status switch
    {
        DeliveryStatus.Delivered or DeliveryStatus.Closed => "Delivered",
        DeliveryStatus.PartiallyDelivered => "PartiallyDelivered",
        DeliveryStatus.Failed => "Failed",
        DeliveryStatus.Refused => "Refused",
        DeliveryStatus.Planned or DeliveryStatus.Assigned => "Pending",
        _ => "InTransit",
    };

    public async Task<IReadOnlyList<DeliveryFact>> DeliveriesAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var list = await InWindow(window).ToListAsync(cancellationToken);
        if (list.Count == 0)
        {
            return [];
        }

        var ids = list.Select(d => d.Id).ToList();
        var attempts = await db.Attempts.AsNoTracking().Where(a => ids.Contains(a.DeliveryId)).GroupBy(a => a.DeliveryId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, cancellationToken);
        var reasons = (await db.Exceptions.AsNoTracking().Where(e => ids.Contains(e.DeliveryId) && (e.ExceptionType == ExceptionType.DeliveryFailed || e.ExceptionType == ExceptionType.CustomerRefusal)).ToListAsync(cancellationToken))
            .GroupBy(e => e.DeliveryId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.RaisedAt).First().Description);
        var names = await NamesAsync(list.Select(d => d.TransporterId), cancellationToken);
        return list.Select(d => new DeliveryFact(
            d.Number, d.ShipmentReference, d.TransporterId, d.TransporterId is { } t ? names.GetValueOrDefault(t) : null, d.CustomerName, d.OriginReference ?? string.Empty, d.DestinationReference ?? string.Empty,
            d.WindowEnd ?? d.PlannedDeliveryAt, d.ActualDeliveryAt, StatusOf(d), d.OnTime, attempts.GetValueOrDefault(d.Id), true, reasons.GetValueOrDefault(d.Id), d.DestinationReference,
            d.Status == DeliveryStatus.Failed ? "Re-attempt" : d.Status == DeliveryStatus.Refused ? "Return or reschedule" : null, Day(d.ActualDeliveryAt ?? d.PlannedDeliveryAt))).ToList();
    }

    public async Task<IReadOnlyList<PodFact>> PodsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var deliveries = (await InWindow(window).Where(d => d.ActualDeliveryAt != null).ToListAsync(cancellationToken)).Where(d => d.Status is DeliveryStatus.Delivered or DeliveryStatus.PartiallyDelivered or DeliveryStatus.Closed).ToList();
        if (deliveries.Count == 0)
        {
            return [];
        }

        var ids = deliveries.Select(d => d.Id).ToList();
        var pods = (await db.Pods.AsNoTracking().Where(p => ids.Contains(p.DeliveryId) && p.IsCurrent).ToListAsync(cancellationToken)).GroupBy(p => p.DeliveryId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PodVersion).First());
        var sla = TimeSpan.FromHours((await settings.GetAsync<SlaSetting>(DeliverySettingKeys.Sla, cancellationToken)).PodSubmissionHours);
        var names = await NamesAsync(deliveries.Select(d => d.TransporterId), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return deliveries.Select(d =>
        {
            pods.TryGetValue(d.Id, out var p);
            var done = d.ActualDeliveryAt!.Value;
            var first = p?.FirstSubmittedAt;
            bool? within = first is { } f ? f - done <= sla : now - done > sla ? false : null;
            var status = p is null ? "Pending" : p.Status switch
            {
                PodStatus.Submitted or PodStatus.UnderReview => "Submitted",
                PodStatus.Accepted => "Accepted",
                PodStatus.Rejected => "Rejected",
                PodStatus.ResubmissionRequired => "ResubmissionRequired",
                _ => "Pending",
            };
            return new PodFact(p?.PodNumber ?? $"POD-{d.Number}", d.Number, d.ShipmentReference, d.TransporterId, d.TransporterId is { } t ? names.GetValueOrDefault(t) : null, d.CustomerName, true, done, first,
                p?.ReviewedAt ?? p?.ApprovedAt, status, p?.ReturnedAt, p?.ReviewedBy is null ? null : "POD reviewer", p?.RejectionReason, p?.ResubmittedAt, within, Day(done));
        }).ToList();
    }

    public async Task<IReadOnlyList<DiscrepancyFact>> DiscrepanciesAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var deliveries = await InWindow(window).Where(d => d.ActualDeliveryAt != null).ToListAsync(cancellationToken);
        if (deliveries.Count == 0)
        {
            return [];
        }

        var byId = deliveries.ToDictionary(d => d.Id);
        var ids = byId.Keys.ToList();
        var rows = await db.Discrepancies.AsNoTracking().Where(x => ids.Contains(x.DeliveryId) && (x.Type == DiscrepancyType.Shortage || x.Type == DiscrepancyType.Damage)).ToListAsync(cancellationToken);
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.DeliveryId)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var podOwner = await db.Pods.AsNoTracking().Where(p => ids.Contains(p.DeliveryId)).Select(p => new { p.Id, p.DeliveryId }).ToListAsync(cancellationToken);
        var podIds = podOwner.Select(p => p.Id).ToList();
        var withEvidence = (await db.Evidence.AsNoTracking().Where(e => podIds.Contains(e.PodId)).Select(e => e.PodId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var evidence = podOwner.Where(p => withEvidence.Contains(p.Id)).Select(p => p.DeliveryId).ToHashSet();
        var names = await NamesAsync(deliveries.Select(d => d.TransporterId), cancellationToken);
        return rows.Select(x =>
        {
            var d = byId[x.DeliveryId];
            items.TryGetValue(x.DeliveryItemId, out var item);
            return new DiscrepancyFact(d.Number, d.ShipmentReference, d.CustomerName, d.TransporterId, d.TransporterId is { } t ? names.GetValueOrDefault(t) : null, item?.SkuReference ?? "Unknown SKU", item?.OrderedQuantity ?? 0m,
                item?.DispatchedQuantity ?? 0m, item?.DeliveredQuantity ?? 0m, x.Quantity, x.Type.ToString(), x.ReasonCode ?? x.Description, item?.DamageType, evidence.Contains(x.DeliveryId), x.ClaimReference, null, Day(d.ActualDeliveryAt!.Value));
        }).ToList();
    }

    public async Task<IReadOnlyList<ExceptionFact>> ExceptionsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var query = db.Exceptions.AsNoTracking().AsQueryable();
        if (window.TransporterId is { } own)
        {
            query = query.Where(e => e.TransporterId == own);
        }

        var list = await query.OrderByDescending(e => e.RaisedAt).Take(2_000).ToListAsync(cancellationToken);
        var deliveries = await db.Deliveries.AsNoTracking().Where(d => list.Select(e => e.DeliveryId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var names = await NamesAsync(list.Select(e => e.TransporterId), cancellationToken);
        return list.Select(e =>
        {
            deliveries.TryGetValue(e.DeliveryId, out var d);
            return new ExceptionFact("Delivery", e.Number, e.ExceptionType.ToString(), e.Severity switch { ExceptionSeverity.Critical => "Critical", ExceptionSeverity.High => "High", ExceptionSeverity.Medium => "Warning", _ => "Info" },
                d?.ShipmentReference, e.TransporterId, e.TransporterId is { } t ? names.GetValueOrDefault(t) : null, d is null ? null : $"{d.OriginReference} → {d.DestinationReference}", e.RaisedAt, e.ResolvedAt, e.Department,
                e.Status switch { ExceptionStatus.Resolved or ExceptionStatus.Closed => "Resolved", ExceptionStatus.Open => "Open", _ => "InProgress" });
        }).ToList();
    }
}
