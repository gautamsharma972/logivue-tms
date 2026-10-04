using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class VehicleEvaluatorTests
{
    [Fact]
    public void A_load_over_the_payload_is_rejected_with_the_amount()
    {
        var e = VehicleEvaluator.Evaluate(16_000m, 10m, Type("15T", 15_000, 60m));

        e.Accepted.ShouldBeFalse();
        e.RejectionCode.ShouldBe(UnplannedCodes.PayloadExceeded);
        e.Reason.ShouldNotBeNull().ShouldContain("1000");
    }

    [Fact]
    public void A_load_over_the_volume_is_rejected_even_when_the_weight_fits()
    {
        var e = VehicleEvaluator.Evaluate(5_000m, 70m, Type("32ft", 15_000, 65m));

        e.Accepted.ShouldBeFalse();
        e.RejectionCode.ShouldBe(UnplannedCodes.VolumeExceeded);
    }

    [Fact]
    public void Weight_and_volume_utilisation_are_reported_separately()
    {
        var e = VehicleEvaluator.Evaluate(10_500m, 49m, Type("32ft", 15_000, 65m));

        e.Accepted.ShouldBeTrue();
        e.WeightUtilisation.ShouldBe(0.7m);
        e.VolumeUtilisation.ShouldBe(0.7538m);
    }

    [Fact]
    public void Inactive_vehicle_types_are_not_considered()
    {
        VehicleEvaluator.Evaluate(100m, null, [Type("old", 5000, active: false), Type("new", 5000)]).Select(x => x.Name).ShouldBe(["new"]);
    }
}

public class RuleBasedPlanningOptimizerTests
{
    /// <summary>Prices by a simple table: FTL by vehicle name, PTL per kg, so each scenario controls exactly what is cheapest.</summary>
    private sealed class FakeQuotes(Func<FreightQuoteRequest, decimal?> price) : IFreightQuoteService
    {
        public int Calls { get; private set; }

        public Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            var total = price(request);
            IReadOnlyList<FreightQuoteResult> quotes = total is null
                ? []
                : [new FreightQuoteResult(Guid.NewGuid(), "CN-1", Transporter, "Shree Roadlines", request.Mode ?? FreightMode.Ftl, "lane", null, [new FreightQuoteLine("FREIGHT", "Freight", total.Value)], [], total.Value)];
            return Task.FromResult(new FreightQuoteSet(quotes, total is null ? "no rate" : null));
        }
    }

    private static PlannableOrder Plannable(Order o) =>
        new(o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, o.DeliverByDate);

    private static PlanningInput Input(IEnumerable<Order> orders, PlanOptions? options = null, IReadOnlyList<PlannedVehicle>? locked = null) =>
        new(Today, orders.Select(Plannable).ToList(), Types, options ?? new PlanOptions(), locked ?? []);

    private static readonly Func<FreightQuoteRequest, decimal?> Standard = r =>
        r.Mode == FreightMode.Ptl ? r.WeightKg * 10m : r.VehicleTypeId == Truck32.Id ? 30_000m : r.VehicleTypeId == Truck14.Id ? 12_000m : 4_000m;

    [Fact]
    public async Task The_smallest_feasible_vehicle_is_chosen_when_it_is_cheaper_and_alternatives_say_why_not()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));

        var plan = await optimizer.OptimizeAsync(Input([NewOrder(3_000m, 10m)], new PlanOptions(AllowPtl: false)), default);

        var vehicle = plan.Vehicles.ShouldHaveSingleItem();
        vehicle.VehicleTypeName.ShouldBe("14ft");
        vehicle.EstimatedCost.ShouldBe(12_000m);
        vehicle.WeightUtilisation.ShouldBe(0.75m);
        vehicle.Alternatives.Single(a => a.VehicleTypeName == "32ft" && a.Total.HasValue).Verdict.ShouldContain("more");
        vehicle.Alternatives.Single(a => a.VehicleTypeName == "Ace").Verdict.ShouldContain("Payload exceeded");
        vehicle.Reason.ShouldContain("14ft");
        plan.SolverStatus.ShouldBe(SolverStatus.Feasible);
    }

    [Fact]
    public async Task Part_load_is_chosen_when_it_is_cheaper_and_full_truck_when_it_is_not()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));

        var small = await optimizer.OptimizeAsync(Input([NewOrder(200m, 1m)]), default);
        var large = await optimizer.OptimizeAsync(Input([NewOrder(3_900m, 10m)]), default); // PTL 39,000 vs 14ft 12,000

        small.Vehicles.Single().Mode.ShouldBe(FreightMode.Ptl); // PTL 2,000 vs Ace 4,000
        large.Vehicles.Single().Mode.ShouldBe(FreightMode.Ftl);
    }

    [Fact]
    public async Task Disallowing_part_load_forces_a_full_truck()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));

        var plan = await optimizer.OptimizeAsync(Input([NewOrder(200m, 1m)], new PlanOptions(AllowPtl: false)), default);

        plan.Vehicles.Single().Mode.ShouldBe(FreightMode.Ftl);
    }

    [Fact]
    public async Task Compatible_orders_are_consolidated_and_the_saving_is_reported()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));
        var orders = new[] { NewOrder(1_500m, 5m), NewOrder(1_500m, 5m) };

        var plan = await optimizer.OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false)), default);

        var vehicle = plan.Vehicles.ShouldHaveSingleItem();
        vehicle.Orders.Count.ShouldBe(2);
        vehicle.VehicleTypeName.ShouldBe("14ft");
        vehicle.ConsolidationSaving.ShouldBe(12_000m); // one 14ft (12,000) instead of two (24,000)
    }

    [Fact]
    public async Task Without_consolidation_each_order_gets_its_own_vehicle()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));
        var orders = new[] { NewOrder(1_500m, 5m), NewOrder(1_500m, 5m) };

        var plan = await optimizer.OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false, AllowConsolidation: false)), default);

        plan.Vehicles.Count.ShouldBe(2);
        plan.Summary.TotalCost.ShouldBe(24_000m);
    }

    [Fact]
    public async Task Max_stops_splits_a_consolidated_group()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));
        var orders = Enumerable.Range(0, 5).Select(_ => NewOrder(100m, 1m)).ToList();

        var plan = await optimizer.OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false, MaxStops: 2)), default);

        plan.Vehicles.Select(v => v.Orders.Count).OrderBy(x => x).ToArray().ShouldBe([1, 2, 2]);
    }

    [Fact]
    public async Task An_order_no_vehicle_can_carry_is_unplanned_with_a_reason_and_suggestions()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));

        var plan = await optimizer.OptimizeAsync(Input([NewOrder(20_000m, 10m)], new PlanOptions(AllowPtl: false)), default);

        plan.Vehicles.ShouldBeEmpty();
        var unplanned = plan.Unplanned.ShouldHaveSingleItem();
        unplanned.Code.ShouldBe(UnplannedCodes.PayloadExceeded);
        unplanned.Reason.ShouldContain("kg");
        unplanned.Suggestions.ShouldNotBeEmpty();
        plan.SolverStatus.ShouldBe(SolverStatus.Infeasible);
    }

    [Fact]
    public async Task A_shipment_bigger_than_any_vehicle_cannot_move_as_part_load_either()
    {
        // PTL has a rate for any weight here, which must not make a 25-tonne order plannable.
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(r => r.Mode == FreightMode.Ptl ? r.WeightKg * 5m : Standard(r)));

        var plan = await optimizer.OptimizeAsync(Input([NewOrder(25_000m, 10m)], new PlanOptions()), default);

        plan.Vehicles.ShouldBeEmpty();
        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.PayloadExceeded);
    }

    [Fact]
    public async Task An_order_with_no_rate_is_unplanned_not_silently_dropped_or_priced_at_zero()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(_ => null));

        var plan = await optimizer.OptimizeAsync(Input([NewOrder(1_000m, 5m)]), default);

        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.NoRate);
    }

    [Fact]
    public async Task Every_order_ends_up_either_planned_or_unplanned()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));
        var orders = new[] { NewOrder(1_000m, 3m), NewOrder(25_000m, 3m), NewOrder(500m, 1m, direction: OrderDirection.Reverse, pickup: Customer("Shah", "Surat", "Gujarat"), drop: Plant) };

        var plan = await optimizer.OptimizeAsync(Input(orders), default);

        var accounted = plan.Vehicles.SelectMany(v => v.Orders.Select(o => o.OrderId)).Concat(plan.Unplanned.Select(u => u.OrderId)).ToList();
        accounted.Order().ShouldBe(orders.Select(o => o.Id).Order());
    }

    [Fact]
    public async Task A_locked_vehicle_is_carried_over_unchanged()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));
        var first = await optimizer.OptimizeAsync(Input([NewOrder(3_000m, 10m)], new PlanOptions(AllowPtl: false)), default);
        var locked = first.Vehicles.Single() with { IsLocked = true };
        var another = NewOrder(1_000m, 2m);

        var second = await optimizer.OptimizeAsync(Input([another], new PlanOptions(AllowPtl: false), [locked]), default);

        second.Vehicles.Count.ShouldBe(2);
        second.Vehicles.ShouldContain(v => v.Key == locked.Key && v.IsLocked && v.Orders.Single().OrderId == locked.Orders.Single().OrderId);
    }

    [Fact]
    public async Task When_the_time_limit_is_hit_the_plan_so_far_is_returned_and_the_rest_listed_as_unplanned()
    {
        var slow = new SlowQuotes(Standard);
        var optimizer = new RuleBasedPlanningOptimizer(slow);
        var orders = Enumerable.Range(0, 6).Select(_ => NewOrder(100m, 1m)).ToList();

        var plan = await optimizer.OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false, AllowConsolidation: false, TimeBudgetSeconds: 1)), default);

        plan.SolverStatus.ShouldBe(SolverStatus.TimeLimitReached);
        plan.Vehicles.ShouldNotBeEmpty();
        plan.Unplanned.ShouldNotBeEmpty();
        plan.Unplanned.ShouldAllBe(u => u.Code == UnplannedCodes.TimeLimit);
        (plan.Vehicles.Sum(v => v.Orders.Count) + plan.Unplanned.Count).ShouldBe(6);
    }

    [Fact]
    public async Task Maximise_utilisation_prefers_the_fuller_truck_even_when_it_costs_more()
    {
        // 14ft costs more than the 32ft here, but is the tighter fit.
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(r => r.Mode == FreightMode.Ptl ? null : r.VehicleTypeId == Truck32.Id ? 10_000m : 15_000m));

        var cheapest = await optimizer.OptimizeAsync(Input([NewOrder(3_500m, 10m)], new PlanOptions(AllowPtl: false)), default);
        var fullest = await optimizer.OptimizeAsync(Input([NewOrder(3_500m, 10m)], new PlanOptions(PlanObjective.MaximizeUtilisation, AllowPtl: false)), default);

        cheapest.Vehicles.Single().VehicleTypeName.ShouldBe("32ft");
        fullest.Vehicles.Single().VehicleTypeName.ShouldBe("14ft");
    }

    [Fact]
    public async Task Comparison_lists_every_vehicle_type_including_those_that_cannot_carry_the_load()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(Standard));
        var order = NewOrder(3_000m, 10m);

        var comparison = await optimizer.CompareAsync([Plannable(order)], Today, Types, new PlanOptions(), default);

        comparison.Chosen.ShouldNotBeNull();
        comparison.Alternatives.ShouldContain(a => a.VehicleTypeName == "Ace" && a.Total == null);
        comparison.Alternatives.ShouldContain(a => a.Mode == FreightMode.Ptl && a.Total == 30_000m);
    }

    [Fact]
    public async Task A_group_too_big_for_any_vehicle_with_a_rate_is_split_and_consolidated_in_halves_not_sent_one_by_one()
    {
        // The vehicle master has a 28 t trailer nobody has a rate for, so the first packing puts all 24 t in one group.
        var trailer = Type("Trailer", 28_000, 76m);
        var types = new[] { Ace, Truck14, Truck32, trailer };
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(r => r.VehicleTypeId == trailer.Id ? null : Standard(r)));
        var orders = Enumerable.Range(0, 8).Select(_ => NewOrder(3_000m, 5m)).ToList();

        var plan = await optimizer.OptimizeAsync(new PlanningInput(Today, orders.Select(Plannable).ToList(), types, new PlanOptions(AllowPtl: false), []), default);

        plan.Vehicles.Count.ShouldBe(2); // 4 × 3 t on each 32 ft truck, not eight separate vehicles
        plan.Vehicles.ShouldAllBe(v => v.VehicleTypeName == "32ft" && v.Orders.Count == 4);
        plan.Summary.TotalCost.ShouldBe(60_000m);
        plan.Unplanned.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_split_still_ends_in_single_orders_when_grouping_never_pays()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(r => r.Drops > 1 ? 50_000m : 1_000m)); // any multi-drop trip is absurdly dear
        var orders = Enumerable.Range(0, 4).Select(_ => NewOrder(900m, 2m)).ToList();

        var plan = await optimizer.OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false)), default);

        plan.Summary.TotalCost.ShouldBe(4_000m);
        plan.Vehicles.Sum(v => v.Orders.Count).ShouldBe(4);
    }

    [Fact]
    public async Task Part_load_is_not_offered_above_the_configured_weight_even_when_it_is_cheaper()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new FakeQuotes(r => r.Mode == FreightMode.Ptl ? r.WeightKg : Standard(r))); // PTL ₹1/kg: 6,000 kg = ₹6,000, truck ₹30,000
        var heavy = NewOrder(6_000m, 10m);

        var capped = await optimizer.OptimizeAsync(Input([heavy], new PlanOptions()), default);
        var raised = await new RuleBasedPlanningOptimizer(new FakeQuotes(r => r.Mode == FreightMode.Ptl ? r.WeightKg : Standard(r)))
            .OptimizeAsync(Input([heavy], new PlanOptions(MaxPtlWeightKg: 10_000)), default);

        capped.Vehicles.Single().Mode.ShouldBe(FreightMode.Ftl);
        raised.Vehicles.Single().Mode.ShouldBe(FreightMode.Ptl);
    }

    private sealed class SlowQuotes(Func<FreightQuoteRequest, decimal?> price) : IFreightQuoteService
    {
        public async Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(400, cancellationToken);
            return await new FakeQuotes(price).QuoteAsync(request, cancellationToken);
        }
    }
}
