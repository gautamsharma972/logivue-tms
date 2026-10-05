using Tms.Modules.Deliveries.Domain;

namespace Tms.Modules.Deliveries.Application.Dashboard;

/// <summary>One delivery with its current proof, reduced to what the dashboard measures. Exact timestamps only.</summary>
internal sealed record ProofRow(
    Guid DeliveryId, string Number, string Customer, string? Transporter, Guid? TransporterId, string? Origin, string? Destination, DeliveryStatus Status, DeliveryOutcome? Outcome,
    DateTimeOffset Planned, DateTimeOffset? Delivered, DateTimeOffset? WindowEnd, Guid? PodId, PodStatus? PodStatus, DateTimeOffset? FirstSubmittedAt, DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt, DateTimeOffset? ReviewedAt, DateTimeOffset? ReturnedAt, DateTimeOffset? ResubmittedAt, int Rejections)
{
    /// <summary>A delivery that was made and so needs a proof. Failed and refused deliveries do not: for them proof compliance is not applicable, not zero.</summary>
    public bool NeedsProof => Status is DeliveryStatus.Delivered or DeliveryStatus.PartiallyDelivered or DeliveryStatus.Closed && Outcome is not (DeliveryOutcome.Failed or DeliveryOutcome.Refused) && Delivered is not null;

    public string Lane => $"{Origin ?? "?"} → {Destination ?? "?"}";

    /// <summary>Where the item waits, or null when it is not waiting on anyone (accepted, or not yet delivered).</summary>
    public AgeingStage? Stage => !NeedsProof || Status == DeliveryStatus.Closed
        ? null
        : PodStatus switch
        {
            null or Domain.PodStatus.Pending or Domain.PodStatus.Draft or Domain.PodStatus.Captured => AgeingStage.PendingSubmission,
            Domain.PodStatus.Submitted or Domain.PodStatus.UnderReview => AgeingStage.PendingReview,
            Domain.PodStatus.Rejected => AgeingStage.Rejected,
            Domain.PodStatus.ResubmissionRequired => AgeingStage.ResubmissionRequired,
            _ => null,
        };

    public DateTimeOffset? WaitingSince => Stage switch
    {
        AgeingStage.PendingSubmission => Delivered,
        AgeingStage.PendingReview => SubmittedAt ?? FirstSubmittedAt,
        AgeingStage.Rejected or AgeingStage.ResubmissionRequired => ReturnedAt,
        _ => null,
    };
}

internal static class ComplianceCalculator
{
    private static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round((decimal)part / whole, 4);

    private static decimal? Average(IEnumerable<double> hours)
    {
        var list = hours.ToList();
        return list.Count == 0 ? null : Math.Round((decimal)list.Average(), 1);
    }

    public static ComplianceMetricsDto Compute(IEnumerable<ProofRow> rows, SlaSetting sla, DateTimeOffset now)
    {
        var made = rows.Where(r => r.NeedsProof).ToList();
        var submitted = made.Where(r => r.FirstSubmittedAt is not null).ToList();
        var submission = TimeSpan.FromHours(sla.PodSubmissionHours);

        // Compliance is judged on the deliveries whose submission target has passed or that were submitted: one still inside its target has not failed.
        var judged = made.Where(r => r.FirstSubmittedAt is not null || now - r.Delivered!.Value > submission).ToList();
        var within = judged.Count(r => r.FirstSubmittedAt is { } first && first - r.Delivered!.Value <= submission);

        var timed = made.Where(r => r.WindowEnd is not null).ToList();
        return new ComplianceMetricsDto(
            made.Count, submitted.Count, made.Count(r => r.FirstSubmittedAt is null), submitted.Count(r => r.Rejections > 0),
            submitted.Count(r => r.PodStatus == Domain.PodStatus.Accepted),
            Rate(within, judged.Count),
            Rate(submitted.Count(r => r.PodStatus == Domain.PodStatus.Accepted), submitted.Count),
            Rate(submitted.Count(r => r.Rejections > 0), submitted.Count),
            Average(submitted.Select(r => Ageing.Hours(r.Delivered!.Value, r.FirstSubmittedAt!.Value))),
            Average(submitted.Where(r => r.ReviewedAt is not null && (r.SubmittedAt ?? r.FirstSubmittedAt) is not null && r.ReviewedAt >= (r.SubmittedAt ?? r.FirstSubmittedAt)).Select(r => Ageing.Hours((r.SubmittedAt ?? r.FirstSubmittedAt)!.Value, r.ReviewedAt!.Value))),
            Average(made.Where(r => r.ReturnedAt is not null && r.ResubmittedAt is not null).Select(r => Ageing.Hours(r.ReturnedAt!.Value, r.ResubmittedAt!.Value))),
            Rate(timed.Count(r => r.Delivered <= r.WindowEnd), timed.Count));
    }
}

internal static class AgeingCalculator
{
    public sealed record Aged(ProofRow Row, AgeingStage Stage, double Hours, int Bucket, bool Overdue, int Target);

    public static IReadOnlyList<Aged> Age(IEnumerable<ProofRow> rows, SlaSetting sla, AgeingSetting buckets, DateTimeOffset now) =>
        rows.Where(r => r.Stage is not null && r.WaitingSince is not null).Select(r =>
        {
            var stage = r.Stage!.Value;
            var hours = Ageing.Hours(r.WaitingSince!.Value, now);
            var target = Ageing.TargetHours(stage, sla);
            return new Aged(r, stage, hours, Ageing.BucketIndex(hours, buckets), hours > target, target);
        }).ToList();
}
