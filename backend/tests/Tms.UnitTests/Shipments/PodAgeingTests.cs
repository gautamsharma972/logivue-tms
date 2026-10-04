using Tms.Modules.Shipments.Application.Delivery;

namespace Tms.UnitTests.Shipments;

public class PodAgeingTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static AgeingDto Summarise(params (int Age, Guid? Transporter, bool Exception)[] rows) =>
        AgeingHandler.Summarise(rows, 7, id => id == A ? "Alpha" : "Beta");

    [Fact]
    public void Outstanding_proofs_fall_into_age_buckets_and_the_boundaries_are_inclusive()
    {
        var result = Summarise((0, A, false), (3, A, false), (4, A, false), (7, B, false), (8, B, false), (15, B, false), (16, B, false), (30, B, false), (31, B, false), (90, B, false));

        result.Buckets.Select(b => (b.Label, b.Count)).ToArray().ShouldBe([("0–3 days", 2), ("4–7 days", 2), ("8–15 days", 2), ("16–30 days", 2), ("Over 30 days", 2)]);
        result.Outstanding.ShouldBe(10);
    }

    [Fact]
    public void Overdue_means_at_or_past_the_threshold_and_transporters_are_ranked_by_it()
    {
        var result = Summarise((9, A, false), (2, A, false), (7, B, false), (20, B, false), (30, B, false));

        result.Overdue.ShouldBe(4); // 9, 7, 20, 30 are at least 7 days old
        result.Transporters.Select(t => (t.Name, t.Outstanding, t.Overdue, t.OldestDays)).ToArray().ShouldBe([("Beta", 3, 3, 30), ("Alpha", 2, 1, 9)]);
    }

    [Fact]
    public void Deliveries_with_shortage_or_damage_are_counted_separately()
    {
        Summarise((1, A, true), (1, A, false), (20, B, true)).WithExceptions.ShouldBe(2);
    }

    [Fact]
    public void Nothing_outstanding_gives_empty_buckets_not_an_error()
    {
        var result = Summarise();

        result.Outstanding.ShouldBe(0);
        result.Buckets.ShouldAllBe(b => b.Count == 0);
        result.Transporters.ShouldBeEmpty();
    }
}
