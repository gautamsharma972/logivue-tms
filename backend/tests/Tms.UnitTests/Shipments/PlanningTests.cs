using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class VehicleSizerTests
{
    [Fact]
    public void The_smallest_vehicle_that_carries_the_load_is_recommended_regardless_of_input_order()
    {
        var result = VehicleSizer.Recommend(3000m, 10m, Types);

        result.Recommended!.Name.ShouldBe("14ft");
        result.Fitting.Select(f => f.Name).ShouldBe(["14ft", "32ft"], "smallest first");
        result.Recommended.WeightUtilization.ShouldBe(0.75m);
        result.Recommended.VolumeUtilization.ShouldBe(0.5556m);
    }

    [Fact]
    public void Volume_can_force_a_bigger_vehicle_than_weight_alone_would()
    {
        // 600 kg fits an Ace by weight (750 kg), but 10 CBM does not fit its 3.5 CBM body.
        VehicleSizer.Recommend(600m, 10m, Types).Recommended!.Name.ShouldBe("14ft");
        VehicleSizer.Recommend(600m, 3m, Types).Recommended!.Name.ShouldBe("Ace");
    }

    [Fact]
    public void A_load_exactly_at_capacity_still_fits()
    {
        VehicleSizer.Recommend(750m, 3.5m, Types).Recommended!.Name.ShouldBe("Ace");
        VehicleSizer.Recommend(750.01m, 3.5m, Types).Recommended!.Name.ShouldBe("14ft");
    }

    [Fact]
    public void Without_a_volume_only_weight_decides()
    {
        VehicleSizer.Recommend(700m, null, Types).Recommended!.Name.ShouldBe("Ace");
    }

    [Fact]
    public void A_vehicle_type_with_no_known_volume_is_not_rejected_for_volume()
    {
        var noVolume = Type("Mystery", 5000);

        VehicleSizer.Recommend(1000m, 50m, [noVolume]).Recommended!.Name.ShouldBe("Mystery");
    }

    [Fact]
    public void When_nothing_fits_it_says_how_many_of_the_largest_are_needed()
    {
        var byWeight = VehicleSizer.Recommend(40_000m, 10m, Types);
        byWeight.Recommended.ShouldBeNull();
        byWeight.VehiclesNeeded.ShouldBe(3); // 40,000 / 16,000
        byWeight.Warning.ShouldNotBeNull().ShouldContain("3 × 32ft");

        VehicleSizer.Recommend(1000m, 200m, Types).VehiclesNeeded.ShouldBe(4); // 200 / 65 CBM
    }

    [Fact]
    public void Inactive_types_are_ignored_and_no_types_is_explained()
    {
        VehicleSizer.Recommend(500m, null, [Type("Retired", 1000, active: false), Truck14]).Recommended!.Name.ShouldBe("14ft");
        VehicleSizer.Recommend(500m, null, []).Warning.ShouldBe("No vehicle types are set up.");
    }
}

public class ModeAdvisorTests
{
    [Fact]
    public void The_cheaper_mode_wins_and_the_reason_says_why()
    {
        var ftl = ModeAdvisor.Recommend(30_000m, 38_000m, 0.9m);
        ftl.Mode.ShouldBe(FreightMode.Ftl);
        ftl.Reason.ShouldContain("full truck");

        var ptl = ModeAdvisor.Recommend(45_000m, 12_500m, 0.2m);
        ptl.Mode.ShouldBe(FreightMode.Ptl);
        ptl.Reason.ShouldContain("only 20% full");
    }

    [Fact]
    public void When_only_one_mode_has_a_rate_that_one_is_recommended()
    {
        ModeAdvisor.Recommend(30_000m, null, 0.9m).Mode.ShouldBe(FreightMode.Ftl);
        ModeAdvisor.Recommend(null, 9_000m, null).Mode.ShouldBe(FreightMode.Ptl);
    }

    [Fact]
    public void With_no_rates_at_all_there_is_no_recommendation_but_an_explanation()
    {
        var none = ModeAdvisor.Recommend(null, null, null);

        none.Mode.ShouldBeNull();
        none.Reason.ShouldContain("No contract has a rate");
    }

    [Fact]
    public void A_price_tie_is_broken_by_how_full_the_truck_would_be()
    {
        ModeAdvisor.Recommend(10_000m, 10_000m, 0.8m).Mode.ShouldBe(FreightMode.Ftl);
        ModeAdvisor.Recommend(10_000m, 10_000m, 0.3m).Mode.ShouldBe(FreightMode.Ptl);
        ModeAdvisor.Recommend(10_000m, 10_000m, 0.5m).Mode.ShouldBe(FreightMode.Ftl, "exactly at the threshold counts as well used");
    }
}

public class ConsolidationPlannerTests
{
    private static PlannableOrder O(string number, decimal kg, string dropCity = "SURAT", string dropState = "GUJARAT", string pickupCity = "PUNE", int readyOffset = 0,
        OrderDirection dir = OrderDirection.Forward, decimal? cbm = null, int? deadlineOffset = null, string pickupState = "MAHARASHTRA") =>
        new(Guid.NewGuid(), number, dir, pickupState, pickupCity, dropState, dropCity, kg, cbm, Today.AddDays(readyOffset), deadlineOffset is { } d ? Today.AddDays(d) : null);

    private static IReadOnlyList<SuggestedLoad> Suggest(params PlannableOrder[] orders) => ConsolidationPlanner.Suggest(orders, Types);

    [Fact]
    public void Orders_from_one_pickup_to_one_state_ready_together_become_one_load()
    {
        var loads = Suggest(O("A", 5000, "SURAT"), O("B", 4000, "VAPI"), O("C", 2000, "NAVSARI"));

        var load = loads.ShouldHaveSingleItem();
        load.OrderIds.Count.ShouldBe(3);
        load.TotalWeightKg.ShouldBe(11_000m);
        load.Drops.ShouldBe(["NAVSARI, GUJARAT", "SURAT, GUJARAT", "VAPI, GUJARAT"]);
        load.Vehicle!.Name.ShouldBe("32ft");
        load.Utilization.ShouldBe(0.6875m);
        load.Suggested.ShouldBe(FreightMode.Ftl);
    }

    [Fact]
    public void Different_pickups_and_different_destination_states_are_never_mixed()
    {
        var loads = Suggest(O("A", 3000), O("B", 3000, pickupCity: "NASHIK"), O("C", 3000, "JAIPUR", "RAJASTHAN"));

        loads.Count.ShouldBe(3);
        loads.Select(l => (l.PickupCity, l.Drops[0])).ShouldBe(
            [("NASHIK", "SURAT, GUJARAT"), ("PUNE", "JAIPUR, RAJASTHAN"), ("PUNE", "SURAT, GUJARAT")], ignoreOrder: true);
    }

    [Fact]
    public void Orders_ready_far_apart_are_not_held_together()
    {
        var loads = Suggest(O("A", 3000, readyOffset: 0), O("B", 3000, readyOffset: 2), O("C", 3000, readyOffset: 5));

        loads.Count.ShouldBe(2);
        loads.Select(l => l.OrderIds.Count).Order().ShouldBe([1, 2]);
    }

    [Fact]
    public void A_load_that_would_overflow_the_biggest_vehicle_is_split_first_fit_decreasing()
    {
        // Capacity 16,000 kg, heaviest first: 9k→bin1; 8k won't join it→bin2; 6k joins bin1 (15k); 5k joins bin2 (13k); 4k fits neither→bin3.
        var loads = Suggest(O("A", 9000), O("B", 8000), O("C", 6000), O("D", 5000), O("E", 4000));

        loads.Select(l => l.TotalWeightKg).Order().ToList().ShouldBe([4_000m, 13_000m, 15_000m]);
        loads.ShouldAllBe(l => l.TotalWeightKg <= 16_000m);
        loads.SelectMany(l => l.OrderIds).Count().ShouldBe(5, "every order is in exactly one load");
        loads.SelectMany(l => l.OrderIds).Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void Volume_limits_packing_as_well_as_weight()
    {
        var loads = Suggest(O("A", 1000, cbm: 40m), O("B", 1000, cbm: 40m)); // 80 CBM > the 65 CBM body

        loads.Count.ShouldBe(2);
    }

    [Fact]
    public void A_nearly_empty_truck_is_suggested_as_part_load_instead()
    {
        var small = Suggest(O("A", 300)).ShouldHaveSingleItem();
        small.Vehicle!.Name.ShouldBe("Ace");
        small.Utilization.ShouldBe(0.4m);
        small.Suggested.ShouldBe(FreightMode.Ptl);

        Suggest(O("B", 14_000)).ShouldHaveSingleItem().Suggested.ShouldBe(FreightMode.Ftl);
    }

    [Fact]
    public void One_order_bigger_than_any_vehicle_still_gets_a_load_with_a_warning()
    {
        var load = Suggest(O("A", 40_000)).ShouldHaveSingleItem();

        load.Vehicle.ShouldBeNull();
        load.Warning.ShouldNotBeNull().ShouldContain("3 × 32ft");
        load.Suggested.ShouldBe(FreightMode.Ptl);
    }

    [Fact]
    public void Reverse_orders_travelling_the_opposite_way_are_offered_as_a_return_load()
    {
        var back = O("R1", 2000, dropCity: "PUNE", dropState: "MAHARASHTRA", pickupCity: "SURAT", dir: OrderDirection.Reverse, pickupState: "GUJARAT");
        var unrelated = O("R2", 500, dropCity: "DELHI", dropState: "DELHI", pickupCity: "SURAT", dir: OrderDirection.Reverse, pickupState: "GUJARAT");

        var load = Suggest(O("A", 5000, "SURAT"), back, unrelated).ShouldHaveSingleItem();

        load.OrderIds.Count.ShouldBe(1, "reverse orders are never packed into the forward load");
        load.BackhaulOrderIds.ShouldBe([back.Id]);
    }

    [Fact]
    public void Urgent_loads_come_first_and_the_earliest_deadline_is_reported()
    {
        var loads = Suggest(O("Late", 3000, "SURAT", deadlineOffset: 10), O("Urgent", 3000, "JAIPUR", "RAJASTHAN", deadlineOffset: 2));

        loads.Select(l => l.Drops[0]).ShouldBe(["JAIPUR, RAJASTHAN", "SURAT, GUJARAT"]);
        loads[0].EarliestDeadline.ShouldBe(Today.AddDays(2));
        Suggest(O("NoDeadline", 3000)).Single().EarliestDeadline.ShouldBeNull();
    }

    [Fact]
    public void The_result_is_deterministic_and_empty_without_vehicle_types_or_orders()
    {
        var orders = new[] { O("A", 5000), O("B", 4000, "VAPI"), O("C", 7000, "JAIPUR", "RAJASTHAN") };

        Suggest(orders).Select(l => string.Join(',', l.OrderIds)).ShouldBe(Suggest(orders.Reverse().ToArray()).Select(l => string.Join(',', l.OrderIds)));
        ConsolidationPlanner.Suggest(orders, []).ShouldBeEmpty();
        Suggest().ShouldBeEmpty();
    }
}
