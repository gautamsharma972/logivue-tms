using Tms.Modules.Deliveries.Application.Dashboard;
using Tms.Modules.Deliveries.Domain;

namespace Tms.UnitTests.Deliveries;

public class DashboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly SlaSetting Sla = new(24, 4, 12);
    private static readonly AgeingSetting Buckets = new([1, 3, 7, 15, 30]);

    private static ProofRow Row(
        DeliveryStatus status = DeliveryStatus.Delivered, PodStatus? pod = null, double deliveredHoursAgo = 2, double? submittedHoursAfter = null, int rejections = 0, string customer = "ABC",
        DeliveryOutcome? outcome = DeliveryOutcome.Full, double? returnedHoursAgo = null, double? windowOffsetHours = 1, double? reviewedHoursAfterSubmit = null, double? resubmittedHoursAfterReturn = null)
    {
        var delivered = Now.AddHours(-deliveredHoursAgo);
        var submitted = submittedHoursAfter is { } s ? delivered.AddHours(s) : (DateTimeOffset?)null;
        var returned = returnedHoursAgo is { } r ? Now.AddHours(-r) : (DateTimeOffset?)null;
        return new ProofRow(
            Guid.NewGuid(), "DLV-1", customer, "Shree", Guid.NewGuid(), "Pune", "Surat", status, outcome, delivered.AddHours(-1), delivered, windowOffsetHours is { } w ? delivered.AddHours(w) : null,
            pod is null ? null : Guid.NewGuid(), pod, submitted, submitted, pod == PodStatus.Accepted ? submitted?.AddHours(reviewedHoursAfterSubmit ?? 0) : null,
            reviewedHoursAfterSubmit is { } h && submitted is { } sub ? sub.AddHours(h) : null, returned, returned is { } ret && resubmittedHoursAfterReturn is { } re ? ret.AddHours(re) : null, rejections);
    }

    [Fact]
    public void Buckets_are_labelled_from_the_tenants_own_limits()
    {
        Ageing.Labels(Buckets).ShouldBe(["0–1 days", "2–3 days", "4–7 days", "8–15 days", "16–30 days", ">30 days"]);
        Ageing.Labels(new AgeingSetting([2, 10])).ShouldBe(["0–2 days", "3–10 days", ">10 days"]);
        Ageing.Labels(new AgeingSetting([])).Count.ShouldBe(6); // an empty list falls back to the defaults rather than failing
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(23.9, 0)]
    [InlineData(47.9, 0)] // 1 whole day is still "0–1 days"
    [InlineData(48, 1)]
    [InlineData(72, 1)]
    [InlineData(96, 2)]
    [InlineData(24 * 8, 3)]
    [InlineData(24 * 20, 4)]
    [InlineData(24 * 31, 5)]
    [InlineData(24 * 400, 5)]
    public void An_age_in_hours_falls_in_the_right_bucket(double hours, int bucket) => Ageing.BucketIndex(hours, Buckets).ShouldBe(bucket);

    [Fact]
    public void Ageing_uses_exact_timestamps_not_dates()
    {
        // Delivered at 10:00, submitted at 16:00: six hours, whatever the calendar says.
        var delivered = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        Ageing.Hours(delivered, delivered.AddHours(6)).ShouldBe(6);
        Ageing.Hours(delivered, delivered.AddHours(-1)).ShouldBe(0); // never negative
    }

    [Fact]
    public void Each_stage_runs_its_clock_from_the_right_moment_against_its_own_target()
    {
        var pending = Row(deliveredHoursAgo: 30);
        var review = Row(pod: PodStatus.UnderReview, deliveredHoursAgo: 40, submittedHoursAfter: 33); // submitted 7 hours ago
        var rejected = Row(pod: PodStatus.Rejected, deliveredHoursAgo: 60, submittedHoursAfter: 10, returnedHoursAgo: 14, rejections: 1);
        var more = Row(pod: PodStatus.ResubmissionRequired, deliveredHoursAgo: 60, submittedHoursAfter: 10, returnedHoursAgo: 3, rejections: 1);

        var aged = AgeingCalculator.Age([pending, review, rejected, more], Sla, Buckets, Now);

        aged.Select(a => (a.Stage, Math.Round(a.Hours), a.Overdue, a.Target)).ShouldBe([
            (AgeingStage.PendingSubmission, 30d, true, 24),
            (AgeingStage.PendingReview, 7d, true, 4),
            (AgeingStage.Rejected, 14d, true, 12),
            (AgeingStage.ResubmissionRequired, 3d, false, 12),
        ]);
    }

    [Fact]
    public void Nothing_waits_on_an_accepted_failed_refused_or_closed_delivery()
    {
        var rows = new[]
        {
            Row(pod: PodStatus.Accepted, submittedHoursAfter: 1),
            Row(DeliveryStatus.Failed, outcome: DeliveryOutcome.Failed),
            Row(DeliveryStatus.Refused, outcome: DeliveryOutcome.Refused),
            Row(DeliveryStatus.Closed, pod: PodStatus.Accepted, submittedHoursAfter: 1),
        };

        AgeingCalculator.Age(rows, Sla, Buckets, Now).ShouldBeEmpty();
        rows.ShouldAllBe(r => r.Stage == null);
    }

    [Fact]
    public void Compliance_is_not_applicable_when_there_is_nothing_to_measure_rather_than_zero()
    {
        var empty = ComplianceCalculator.Compute([], Sla, Now);
        (empty.SubmissionCompliance, empty.AcceptanceRate, empty.RejectionRate, empty.AverageSubmissionHours, empty.OnTimeRate).ShouldBe((null, null, null, null, null));

        var onlyFailures = ComplianceCalculator.Compute([Row(DeliveryStatus.Failed, outcome: DeliveryOutcome.Failed), Row(DeliveryStatus.Refused, outcome: DeliveryOutcome.Refused)], Sla, Now);
        onlyFailures.Delivered.ShouldBe(0); // proof does not apply to a delivery that was not made
        onlyFailures.SubmissionCompliance.ShouldBeNull();
    }

    [Fact]
    public void A_proof_still_inside_its_target_has_not_failed_it()
    {
        var rows = new[]
        {
            Row(pod: PodStatus.Accepted, deliveredHoursAgo: 10, submittedHoursAfter: 2, reviewedHoursAfterSubmit: 1), // in time
            Row(deliveredHoursAgo: 30), // overdue, not submitted
            Row(deliveredHoursAgo: 5), // still has time: not judged either way
        };

        var m = ComplianceCalculator.Compute(rows, Sla, Now);

        m.Delivered.ShouldBe(3);
        m.PodSubmitted.ShouldBe(1);
        m.PodPending.ShouldBe(2);
        m.SubmissionCompliance.ShouldBe(0.5m); // 1 of the 2 that could be judged
    }

    [Fact]
    public void Acceptance_rejection_and_times_are_measured_from_the_timestamps()
    {
        var rows = new[]
        {
            Row(pod: PodStatus.Accepted, deliveredHoursAgo: 50, submittedHoursAfter: 4, reviewedHoursAfterSubmit: 2),
            Row(pod: PodStatus.Accepted, deliveredHoursAgo: 50, submittedHoursAfter: 8, reviewedHoursAfterSubmit: 4, rejections: 1, returnedHoursAgo: 30, resubmittedHoursAfterReturn: 6),
            Row(pod: PodStatus.UnderReview, deliveredHoursAgo: 50, submittedHoursAfter: 6),
            Row(pod: PodStatus.Rejected, deliveredHoursAgo: 50, submittedHoursAfter: 12, rejections: 1, returnedHoursAgo: 20),
        };

        var m = ComplianceCalculator.Compute(rows, Sla, Now);

        m.PodSubmitted.ShouldBe(4);
        m.PodAccepted.ShouldBe(2);
        m.AcceptanceRate.ShouldBe(0.5m);
        m.RejectionRate.ShouldBe(0.5m);
        m.AverageSubmissionHours.ShouldBe(7.5m); // (4 + 8 + 6 + 12) / 4
        m.AverageReviewHours.ShouldBe(3m); // (2 + 4) / 2
        m.AverageResubmissionHours.ShouldBe(6m); // only the one that was corrected
    }

    [Fact]
    public void On_time_is_judged_only_where_there_was_a_window()
    {
        var rows = new[] { Row(windowOffsetHours: 1), Row(windowOffsetHours: -3), Row(windowOffsetHours: null) };

        ComplianceCalculator.Compute(rows, Sla, Now).OnTimeRate.ShouldBe(0.5m);
    }
}
