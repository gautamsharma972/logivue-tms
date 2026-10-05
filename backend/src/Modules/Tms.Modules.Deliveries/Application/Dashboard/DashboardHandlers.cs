using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application.Notifications;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Application.Dashboard;

/// <summary>Reads deliveries with their current proof as light rows. A transporter's user is held to its own company; everyone else needs read permission.</summary>
internal sealed class ProofRowSource(DeliveriesDbContext db, DeliveryAccess access)
{
    public Guid? Scope(Guid? requested) => access.IsVendor ? access.VendorTransporterId : requested;

    public bool Allowed => access.IsVendor ? access.CanExecute : access.CanRead;

    public IQueryable<RawRow> Rows(Guid? transporterId)
    {
        var deliveries = db.Deliveries.AsNoTracking().AsQueryable();
        if (Scope(transporterId) is { } t)
        {
            deliveries = deliveries.Where(d => d.TransporterId == t);
        }

        // Member-initialiser projection: unlike a constructor, EF can keep filtering on it.
        return from d in deliveries
               join p in db.Pods.AsNoTracking().Where(x => x.IsCurrent) on d.Id equals p.DeliveryId into ps
               from p in ps.DefaultIfEmpty()
               select new RawRow
               {
                   DeliveryId = d.Id, Number = d.Number, Customer = d.CustomerName, Transporter = d.TransporterReference, TransporterId = d.TransporterId, Origin = d.OriginReference, Destination = d.DestinationReference,
                   Status = d.Status, Outcome = d.Outcome, Planned = d.PlannedDeliveryAt, Delivered = d.ActualDeliveryAt, WindowEnd = d.WindowEnd, PodId = p == null ? null : p.Id,
                   PodStatus = p == null ? null : (PodStatus?)p.Status, FirstSubmittedAt = p == null ? null : p.FirstSubmittedAt, SubmittedAt = p == null ? null : p.SubmittedAt,
                   ApprovedAt = p == null ? null : p.ApprovedAt, ReviewedAt = p == null ? null : p.ReviewedAt, ReturnedAt = p == null ? null : p.ReturnedAt,
                   ResubmittedAt = p == null ? null : p.ResubmittedAt, Rejections = p == null ? 0 : p.RejectionCount,
               };
    }

    public static async Task<List<ProofRow>> ToRowsAsync(IQueryable<RawRow> query, int take, CancellationToken cancellationToken) =>
        (await query.Take(take).ToListAsync(cancellationToken)).Select(r => new ProofRow(
            r.DeliveryId, r.Number, r.Customer, r.Transporter, r.TransporterId, r.Origin, r.Destination, r.Status, r.Outcome, r.Planned, r.Delivered, r.WindowEnd, r.PodId, r.PodStatus,
            r.FirstSubmittedAt, r.SubmittedAt, r.ApprovedAt, r.ReviewedAt, r.ReturnedAt, r.ResubmittedAt, r.Rejections)).ToList();

    public static (DateTimeOffset From, DateTimeOffset To, DateOnly FromDay, DateOnly ToDay) Range(DateOnly? from, DateOnly? to, TimeProvider clock)
    {
        var today = clock.TodayInIndia();
        var end = to ?? today;
        var start = from ?? end.AddDays(-30);
        return (new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), Clock.India), new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), Clock.India), start, end);
    }
}

internal sealed class DashboardHandler(
    DeliveriesDbContext db, ProofRowSource source, AgeingService ageing, IDeliverySettings settings, SlaMonitor monitor, NotificationHandler notifications, TimeProvider clock)
{
    private const int RowCap = 20_000;

    public async Task<Result<DashboardSummaryDto>> SummaryAsync(DashboardQuery query, CancellationToken cancellationToken)
    {
        if (!source.Allowed)
        {
            return DeliveryAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var (from, to, fromDay, toDay) = ProofRowSource.Range(query.From, query.To, clock);
        var now = clock.GetUtcNow();
        var todayStart = new DateTimeOffset(clock.TodayInIndia().ToDateTime(TimeOnly.MinValue), Clock.India);

        var rows = await ProofRowSource.ToRowsAsync(source.Rows(query.TransporterId).Where(r => (r.Delivered ?? r.Planned) >= from && (r.Delivered ?? r.Planned) < to), RowCap, cancellationToken);
        var ids = rows.Select(r => r.DeliveryId).ToList();
        var shortage = ids.Count == 0 ? 0 : await db.Discrepancies.AsNoTracking().Where(x => ids.Contains(x.DeliveryId) && x.Type == DiscrepancyType.Shortage).Select(x => x.DeliveryId).Distinct().CountAsync(cancellationToken);
        var damage = ids.Count == 0 ? 0 : await db.Discrepancies.AsNoTracking().Where(x => ids.Contains(x.DeliveryId) && x.Type == DiscrepancyType.Damage).Select(x => x.DeliveryId).Distinct().CountAsync(cancellationToken);

        var exceptions = db.Exceptions.AsNoTracking().Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed);
        if (source.Scope(query.TransporterId) is { } t)
        {
            exceptions = exceptions.Where(e => e.TransporterId == t);
        }

        var open = await exceptions.CountAsync(cancellationToken);
        var overdue = await exceptions.CountAsync(e => e.DueAt < now, cancellationToken);
        var claims = await exceptions.CountAsync(e => e.ClaimReference != null, cancellationToken);
        var unread = await notifications.UnreadCountAsync(cancellationToken);

        int Pods(PodStatus s) => rows.Count(r => r.NeedsProof && r.PodStatus == s);
        return new DashboardSummaryDto(
            fromDay, toDay,
            rows.Count(r => r.Planned >= todayStart && r.Planned < todayStart.AddDays(1)),
            rows.Count(r => r.Status == DeliveryStatus.Delivered), rows.Count(r => r.Status == DeliveryStatus.PartiallyDelivered),
            rows.Count(r => r.Status == DeliveryStatus.Failed), rows.Count(r => r.Status == DeliveryStatus.Refused), rows.Count(r => r.Status == DeliveryStatus.Closed),
            rows.Count(r => r.NeedsProof && r.PodStatus is null or PodStatus.Pending), rows.Count(r => r.NeedsProof && r.PodStatus is PodStatus.Draft or PodStatus.Captured),
            Pods(PodStatus.Submitted), Pods(PodStatus.UnderReview), Pods(PodStatus.Rejected), Pods(PodStatus.ResubmissionRequired), Pods(PodStatus.Accepted),
            shortage, damage, open, overdue, claims, unread);
    }

    public async Task<Result<ComplianceDto>> ComplianceAsync(ComplianceQuery query, CancellationToken cancellationToken)
    {
        if (!source.Allowed)
        {
            return DeliveryAccess.Forbidden;
        }

        var (from, to, fromDay, toDay) = ProofRowSource.Range(query.From, query.To, clock);
        var sla = await settings.GetAsync<SlaSetting>(DeliverySettingKeys.Sla, cancellationToken);
        var rows = source.Rows(query.TransporterId).Where(r => r.Delivered >= from && r.Delivered < to);
        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var customer = query.Customer.Trim();
            rows = rows.Where(r => r.Customer.Contains(customer));
        }

        var list = await ProofRowSource.ToRowsAsync(rows, RowCap, cancellationToken);
        if (!string.IsNullOrWhiteSpace(query.Lane))
        {
            list = list.Where(r => r.Lane.Contains(query.Lane.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var now = clock.GetUtcNow();
        var groupBy = (query.GroupBy ?? "transporter").ToLowerInvariant();
        Func<ProofRow, (string Key, string Name)> key = groupBy switch
        {
            "customer" => r => (r.Customer, r.Customer),
            "lane" => r => (r.Lane, r.Lane),
            _ => r => (r.TransporterId?.ToString() ?? "none", r.Transporter ?? "No transporter"),
        };
        var groups = list.GroupBy(key).OrderBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ComplianceRowDto(g.Key.Key, g.Key.Name, ComplianceCalculator.Compute(g, sla, now))).ToList();
        return new ComplianceDto(fromDay, toDay, groupBy is "customer" or "lane" ? groupBy : "transporter", ComplianceCalculator.Compute(list, sla, now), groups);
    }

    public async Task<Result<AgeingDto>> AgeingAsync(Guid? transporterId, CancellationToken cancellationToken)
    {
        if (!source.Allowed)
        {
            return DeliveryAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var aged = await ageing.AgedAsync(transporterId, cancellationToken);
        var buckets = await settings.GetAsync<AgeingSetting>(DeliverySettingKeys.Ageing, cancellationToken);
        var sla = await settings.GetAsync<SlaSetting>(DeliverySettingKeys.Sla, cancellationToken);
        var labels = Ageing.Labels(buckets);

        var stages = Enum.GetValues<AgeingStage>().Select(stage =>
        {
            var inStage = aged.Where(a => a.Stage == stage).ToList();
            var counts = new int[labels.Count];
            foreach (var a in inStage)
            {
                counts[a.Bucket]++;
            }

            return new AgeingStageDto(stage, StageLabel(stage), inStage.Count, inStage.Count(a => a.Overdue), Ageing.TargetHours(stage, sla), counts);
        }).ToList();

        var totals = new int[labels.Count];
        foreach (var a in aged)
        {
            totals[a.Bucket]++;
        }

        static IReadOnlyList<OverdueCountDto> Top(IEnumerable<AgeingCalculator.Aged> items, Func<ProofRow, string> by) =>
            items.GroupBy(a => by(a.Row)).Select(g => new OverdueCountDto(g.Key, g.Count(a => a.Overdue), g.Count())).Where(x => x.Overdue > 0)
                .OrderByDescending(x => x.Overdue).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(5).ToList();

        return new AgeingDto(labels, stages, totals,
            Top(aged, r => r.Transporter ?? "No transporter"), Top(aged, r => r.Customer), Top(aged, r => r.Destination ?? "Unknown"));
    }

    public async Task<Result<PagedResult<AgeingItemDto>>> AgeingItemsAsync(AgeingItemsQuery query, CancellationToken cancellationToken)
    {
        if (!source.Allowed)
        {
            return DeliveryAccess.Forbidden;
        }

        var aged = (await ageing.AgedAsync(query.TransporterId, cancellationToken)).AsEnumerable();
        var buckets = await settings.GetAsync<AgeingSetting>(DeliverySettingKeys.Ageing, cancellationToken);
        var labels = Ageing.Labels(buckets);
        if (query.Stage is { } stage)
        {
            aged = aged.Where(a => a.Stage == stage);
        }

        if (query.Bucket is { } bucket)
        {
            aged = aged.Where(a => a.Bucket == bucket);
        }

        if (query.OverdueOnly == true)
        {
            aged = aged.Where(a => a.Overdue);
        }

        var ordered = aged.OrderByDescending(a => a.Hours).ToList();
        var size = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(1, query.Page);
        var items = ordered.Skip((page - 1) * size).Take(size).Select(a => new AgeingItemDto(
            a.Stage, a.Row.DeliveryId, a.Row.Number, a.Row.PodId, a.Row.Customer, a.Row.Transporter, a.Row.Destination, Math.Round(a.Hours, 1), labels[a.Bucket], a.Bucket, a.Overdue, a.Target)).ToList();
        return new PagedResult<AgeingItemDto>(items, page, size, ordered.Count);
    }

    public async Task<Result<ExceptionsSummaryDto>> ExceptionsAsync(Guid? transporterId, CancellationToken cancellationToken)
    {
        if (!source.Allowed)
        {
            return DeliveryAccess.Forbidden;
        }

        var rows = db.Exceptions.AsNoTracking().Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed);
        if (source.Scope(transporterId) is { } t)
        {
            rows = rows.Where(e => e.TransporterId == t);
        }

        var list = await rows.Select(e => new { e.ExceptionType, e.Status, e.Severity, e.DueAt }).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        static IReadOnlyList<KeyCountDto> Count<T>(IEnumerable<T> items) where T : struct, Enum => items.GroupBy(i => i).Select(g => new KeyCountDto(g.Key.ToString(), g.Count())).OrderByDescending(x => x.Count).ToList();
        return new ExceptionsSummaryDto(list.Count, list.Count(e => e.DueAt < now), list.Count(e => e.Status == ExceptionStatus.Escalated),
            Count(list.Select(e => e.ExceptionType)), Count(list.Select(e => e.Status)), Count(list.Select(e => e.Severity)));
    }

    public static string StageLabel(AgeingStage stage) => stage switch
    {
        AgeingStage.PendingSubmission => "Pending submission",
        AgeingStage.PendingReview => "Pending review",
        AgeingStage.Rejected => "Rejected",
        _ => "Resubmission pending",
    };
}

/// <summary>Everything that is waiting on a proof, with how long and whether that is past the target.</summary>
internal sealed class AgeingService(ProofRowSource source, IDeliverySettings settings, TimeProvider clock)
{
    private const int RowCap = 20_000;

    public async Task<IReadOnlyList<AgeingCalculator.Aged>> AgedAsync(Guid? transporterId, CancellationToken cancellationToken)
    {
        var open = await ProofRowSource.ToRowsAsync(source.Rows(transporterId)
            .Where(r => r.Delivered != null && r.Status != DeliveryStatus.Closed && r.Status != DeliveryStatus.Failed && r.Status != DeliveryStatus.Refused && r.Status != DeliveryStatus.Cancelled
                && (r.PodStatus == null || r.PodStatus != PodStatus.Accepted && r.PodStatus != PodStatus.Cancelled)), RowCap, cancellationToken);
        return AgeingCalculator.Age(open, await settings.GetAsync<SlaSetting>(DeliverySettingKeys.Sla, cancellationToken), await settings.GetAsync<AgeingSetting>(DeliverySettingKeys.Ageing, cancellationToken), clock.GetUtcNow());
    }
}

/// <summary>The shape EF reads from the database; <see cref="ProofRow"/> is built from it in memory.</summary>
internal sealed class RawRow
{
    public Guid DeliveryId { get; init; }

    public string Number { get; init; } = null!;

    public string Customer { get; init; } = null!;

    public string? Transporter { get; init; }

    public Guid? TransporterId { get; init; }

    public string? Origin { get; init; }

    public string? Destination { get; init; }

    public DeliveryStatus Status { get; init; }

    public DeliveryOutcome? Outcome { get; init; }

    public DateTimeOffset Planned { get; init; }

    public DateTimeOffset? Delivered { get; init; }

    public DateTimeOffset? WindowEnd { get; init; }

    public Guid? PodId { get; init; }

    public PodStatus? PodStatus { get; init; }

    public DateTimeOffset? FirstSubmittedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public DateTimeOffset? ReviewedAt { get; init; }

    public DateTimeOffset? ReturnedAt { get; init; }

    public DateTimeOffset? ResubmittedAt { get; init; }

    public int Rejections { get; init; }
}
