using Tms.Modules.Shipments.Domain;

namespace Tms.UnitTests.Shipments;

public class StopSequencerTests
{
    private static readonly DateTimeOffset Depart = new(2026, 7, 1, 8, 0, 0, TimeSpan.FromMinutes(330));

    /// <summary>Points on a line: depot at 0 km, each stop at its km mark. Distance is |a − b|, 1 km per minute.</summary>
    private static DistanceMatrix Line(params double[] marks)
    {
        var all = marks.Prepend(0d).ToArray();
        var km = new double[all.Length, all.Length];
        var min = new double[all.Length, all.Length];
        for (var i = 0; i < all.Length; i++)
        {
            for (var j = 0; j < all.Length; j++)
            {
                km[i, j] = Math.Abs(all[i] - all[j]);
                min[i, j] = km[i, j];
            }
        }

        return new DistanceMatrix(km, min, RouteSource.Estimate);
    }

    private static SequenceStop Drop(string id, decimal kg = 1000, DateTimeOffset? deadline = null, TimeOnly? from = null, TimeOnly? to = null) =>
        new(id, StopRole.Delivery, kg, null, deadline, from, to);

    private static SequenceStop Pick(string id, decimal kg, DateTimeOffset? deadline = null) => new(id, StopRole.ReturnPickup, kg, null, deadline, null, null);

    private static SequenceProblem Problem(double[] marks, SequenceStop[] stops, bool back = false, decimal? payload = null, int service = 0, decimal? initial = null) =>
        new(Line(marks), stops, Depart, service, back, initial ?? stops.Where(s => s.Role == StopRole.Delivery).Sum(s => s.WeightKg), null, payload, null);

    private static string[] Names(SequenceProblem p, SequenceResult r) => r.Order.Select(i => p.Stops[i].Id).ToArray();

    [Fact]
    public void The_shortest_order_is_found_regardless_of_input_order()
    {
        var p = Problem([30, 10, 20], [Drop("C"), Drop("A"), Drop("B")]);

        var r = StopSequencer.Solve(p);

        Names(p, r).ShouldBe(["A", "B", "C"]);
        r.TotalKm.ShouldBe(30);
        r.Feasible.ShouldBeTrue();
        r.Method.ShouldBe("Exact");
    }

    [Fact]
    public void Returning_to_the_depot_adds_the_return_leg_and_can_change_the_best_order()
    {
        var p = Problem([30, 10, 20], [Drop("C"), Drop("A"), Drop("B")], back: true);

        var r = StopSequencer.Solve(p);

        r.TotalKm.ShouldBe(60); // out to 30 and back
        r.DepotArrival.ShouldNotBeNull();
    }

    [Fact]
    public void A_deadline_forces_a_longer_but_feasible_order()
    {
        // A is far (50) and must be reached within 60 minutes; B is near (10). Shortest overall would be B then A (50), still fine;
        // make B so close to A that going B first breaks A's deadline when each stop takes 30 minutes of service.
        var p = Problem([50, 10], [Drop("A", deadline: Depart.AddMinutes(60)), Drop("B")], service: 30);

        var r = StopSequencer.Solve(p);

        Names(p, r).ShouldBe(["A", "B"]); // B first would reach A at 10+30+40 = 80 min
        r.Feasible.ShouldBeTrue();
        r.TotalKm.ShouldBe(90);
    }

    [Fact]
    public void When_no_order_meets_a_deadline_the_result_is_infeasible_and_says_which()
    {
        var p = Problem([100], [Drop("A", deadline: Depart.AddMinutes(30))]);

        var r = StopSequencer.Solve(p);

        r.Feasible.ShouldBeFalse();
        r.Violation.ShouldNotBeNull().ShouldContain("A");
        r.Violation.ShouldContain("deadline");
    }

    [Fact]
    public void A_delivery_window_makes_the_truck_wait_and_pushes_later_stops()
    {
        var opens = new TimeOnly(10, 0);
        var p = Problem([10, 20], [Drop("A", from: opens, to: new TimeOnly(17, 0)), Drop("B")]);

        var r = StopSequencer.Evaluate(p, [0, 1]);

        r.Timings[0].Arrival.Hour.ShouldBe(8);
        r.Timings[0].WaitMinutes.ShouldBe(110); // arrives 08:10, window opens 10:00
        r.Timings[0].Departure.Hour.ShouldBe(10);
        r.Timings[1].Arrival.ShouldBe(new DateTimeOffset(2026, 7, 1, 10, 10, 0, Depart.Offset));
    }

    [Fact]
    public void Arriving_after_the_window_closes_means_waiting_until_the_next_morning()
    {
        var late = ApplyWindowAt(new DateTimeOffset(2026, 7, 1, 18, 0, 0, Depart.Offset), new TimeOnly(9, 0), new TimeOnly(17, 0));

        late.ShouldBe(new DateTimeOffset(2026, 7, 2, 9, 0, 0, Depart.Offset));
        ApplyWindowAt(new DateTimeOffset(2026, 7, 1, 12, 0, 0, Depart.Offset), new TimeOnly(9, 0), new TimeOnly(17, 0)).Hour.ShouldBe(12);
    }

    private static DateTimeOffset ApplyWindowAt(DateTimeOffset t, TimeOnly from, TimeOnly to) => StopSequencer.ApplyWindow(t, from, to);

    [Fact]
    public void A_return_pickup_that_would_overload_the_truck_is_only_allowed_after_deliveries_free_the_space()
    {
        // 15,000 kg onboard, 16,000 kg payload. The 2,000 kg return is nearest, but picking it up first would carry 17,000 kg.
        var p = Problem([5, 40], [Pick("R", 2000), Drop("D", 15000)], back: true, payload: 16000);

        var r = StopSequencer.Solve(p);

        r.Feasible.ShouldBeTrue();
        Names(p, r).ShouldBe(["D", "R"]);
    }

    [Fact]
    public void A_return_that_cannot_fit_even_after_deliveries_is_infeasible_with_the_overflow_stated()
    {
        var p = Problem([5], [Pick("R", 17000)], back: true, payload: 16000, initial: 0);

        var r = StopSequencer.Solve(p);

        r.Feasible.ShouldBeFalse();
        r.Violation.ShouldNotBeNull().ShouldContain("over by 1000");
    }

    [Fact]
    public void A_return_with_a_deadline_at_the_depot_is_checked_against_the_arrival_back()
    {
        var p = Problem([20], [Pick("R", 100, deadline: Depart.AddMinutes(30))], back: true, payload: 16000, initial: 0);

        var r = StopSequencer.Solve(p);

        r.Feasible.ShouldBeFalse(); // 20 km out + 20 km back = 40 minutes
        r.Violation.ShouldNotBeNull().ShouldContain("depot");
    }

    [Fact]
    public void Larger_routes_use_the_heuristic_say_so_and_never_do_worse_than_the_given_order()
    {
        var marks = new double[] { 90, 10, 80, 20, 70, 30, 60, 40, 50, 100 };
        var stops = marks.Select((_, i) => Drop($"S{i}")).ToArray();
        var p = Problem(marks, stops);

        var solved = StopSequencer.Solve(p);
        var asGiven = StopSequencer.Evaluate(p, Enumerable.Range(0, marks.Length).ToList());

        solved.Method.ShouldBe("Heuristic");
        solved.Feasible.ShouldBeTrue();
        solved.TotalKm.ShouldBeLessThanOrEqualTo(asGiven.TotalKm);
        solved.TotalKm.ShouldBe(100); // a straight run out is optimal on a line
        solved.Order.Distinct().Count().ShouldBe(marks.Length);
    }

    [Fact]
    public void No_stops_is_a_valid_empty_route()
    {
        var r = StopSequencer.Solve(Problem([], []));

        r.Feasible.ShouldBeTrue();
        r.Order.ShouldBeEmpty();
    }
}

public class ReturnsAfterDeliveriesTests
{
    private static readonly DateTimeOffset Depart = new(2026, 7, 1, 8, 0, 0, TimeSpan.FromMinutes(330));

    private static DistanceMatrix Line(params double[] marks)
    {
        var all = marks.Prepend(0d).ToArray();
        var km = new double[all.Length, all.Length];
        for (var i = 0; i < all.Length; i++)
        {
            for (var j = 0; j < all.Length; j++)
            {
                km[i, j] = Math.Abs(all[i] - all[j]);
            }
        }

        return new DistanceMatrix(km, (double[,])km.Clone(), RouteSource.Estimate);
    }

    private static SequenceStop Drop(string id) => new(id, StopRole.Delivery, 1000, null, null, null, null);

    private static SequenceStop Pick(string id) => new(id, StopRole.ReturnPickup, 500, null, null, null, null);

    [Fact]
    public void Without_the_rule_a_return_on_the_way_out_is_collected_first_because_it_is_shorter()
    {
        // Return at 10 km lies on the way to the drop at 50 km (open route: no trip home, so the order matters).
        var p = new SequenceProblem(Line(50, 10), [Drop("D"), Pick("R")], Depart, 0, false, 1000, null, 16000, null);

        var r = StopSequencer.Solve(p);

        r.Order.Select(i => p.Stops[i].Id).ToArray().ShouldBe(["R", "D"]);
        r.TotalKm.ShouldBe(50);
    }

    [Fact]
    public void With_the_rule_every_delivery_comes_before_any_return_even_when_it_costs_distance()
    {
        var p = new SequenceProblem(Line(50, 10), [Drop("D"), Pick("R")], Depart, 0, true, 1000, null, 16000, null, ReturnsAfterDeliveries: true);

        var r = StopSequencer.Solve(p);

        r.Feasible.ShouldBeTrue();
        r.Order.Select(i => p.Stops[i].Id).ToArray().ShouldBe(["D", "R"]);
        r.TotalKm.ShouldBe(50 + 40 + 10); // out to 50, back to the return at 10, home
    }

    [Fact]
    public void A_given_order_that_breaks_the_rule_is_reported()
    {
        var p = new SequenceProblem(Line(50, 10), [Drop("D"), Pick("R")], Depart, 0, true, 1000, null, 16000, null, ReturnsAfterDeliveries: true);

        var r = StopSequencer.Evaluate(p, [1, 0]);

        r.Feasible.ShouldBeFalse();
        r.Violation.ShouldNotBeNull().ShouldContain("deliveries must finish");
    }
}
