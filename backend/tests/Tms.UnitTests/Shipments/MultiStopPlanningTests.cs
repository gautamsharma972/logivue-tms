using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class MultiStopPlanningTests
{
    /// <summary>Points on one meridian: a degree of latitude is 111 km and one minute per km, so distances are easy to reason about.</summary>
    private sealed class MeridianRouting : IRoutingProvider
    {
        private static double Km(GeoPoint a, GeoPoint b) => Math.Round(Math.Abs(a.Latitude - b.Latitude) * 111, 2);

        public Task<RouteResult> GetRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken) =>
            Task.FromResult(new RouteResult(Enumerable.Range(1, waypoints.Count - 1).Select(i => new RouteLeg(Km(waypoints[i - 1], waypoints[i]), Km(waypoints[i - 1], waypoints[i]))).ToList(), RouteSource.Osrm));

        public Task<DistanceMatrix> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken)
        {
            var km = new double[points.Count, points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                for (var j = 0; j < points.Count; j++)
                {
                    km[i, j] = Km(points[i], points[j]);
                }
            }

            return Task.FromResult(new DistanceMatrix(km, (double[,])km.Clone(), RouteSource.Osrm));
        }
    }

    private sealed class Prices(Func<FreightQuoteRequest, decimal?> price) : IFreightQuoteService
    {
        public Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            var total = price(request);
            IReadOnlyList<FreightQuoteResult> quotes = total is null ? [] : [new FreightQuoteResult(Guid.NewGuid(), "CN-1", Transporter, "Shree", request.Mode ?? FreightMode.Ftl, "lane", null, [new FreightQuoteLine("FREIGHT", "Freight", total.Value)], [], total.Value)];
            return Task.FromResult(new FreightQuoteSet(quotes, null));
        }
    }

    private static decimal? Standard(FreightQuoteRequest r) => r.Mode == FreightMode.Ptl ? null : r.VehicleTypeId == Truck14.Id ? 10_000m : r.VehicleTypeId == Truck32.Id ? 30_000m : 4_000m;

    private static RuleBasedPlanningOptimizer Optimizer(Func<FreightQuoteRequest, decimal?>? price = null) => new(new Prices(price ?? Standard), new MeridianRouting());

    private static PlannableOrder At(Order o, double pickupLat, double dropLat, DateOnly? by = null, TimeOnly? from = null, TimeOnly? to = null) =>
        new(o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, by,
            new GeoPoint(pickupLat, 73), new GeoPoint(dropLat, 73), from, to);

    private static Order Forward(string dropCity, decimal kg = 500m) =>
        NewOrder(kg, 2m, drop: Customer("Consignee " + dropCity, dropCity, "Gujarat"));

    private static PlanningInput Input(IEnumerable<PlannableOrder> orders, PlanOptions? options = null) =>
        new(Today, orders.ToList(), Types, options ?? new PlanOptions(AllowPtl: false), []);

    [Fact]
    public async Task Multi_drop_stops_are_re_ordered_into_the_shortest_run()
    {
        var far = Forward("Aaa");
        var near = Forward("Bbb");
        var mid = Forward("Ccc");
        var orders = new[] { At(far, 20, 25), At(near, 20, 22), At(mid, 20, 23) }; // the planner would list them Aaa, Bbb, Ccc

        var vehicle = (await Optimizer().OptimizeAsync(Input(orders), default)).Vehicles.ShouldHaveSingleItem();

        vehicle.Orders.Select(o => o.Number).ToArray().ShouldBe([near.Number, mid.Number, far.Number]);
        vehicle.DistanceKm.ShouldBe(555); // 5 degrees straight out
        vehicle.SequenceMethod.ShouldBe("Exact");
        vehicle.Stops!.Select(s => s.Kind).ToArray().ShouldBe(["Pickup", "Drop", "Drop", "Drop"]);
    }

    [Fact]
    public async Task A_delivery_window_adds_a_wait_to_the_stop_and_the_eta()
    {
        var o = Forward("Aaa");

        var vehicle = (await Optimizer().OptimizeAsync(Input([At(o, 20, 21, from: new TimeOnly(14, 0), to: new TimeOnly(17, 0))]), default)).Vehicles.Single();

        var drop = vehicle.Stops![1];
        drop.PlannedArrival!.Value.Hour.ShouldBe(9); // 08:00 + 111 minutes
        drop.WaitMinutes.ShouldBe(249); // window opens 14:00
        drop.PlannedDeparture!.Value.ShouldBe(new DateTimeOffset(2026, 7, 1, 14, 30, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task A_window_that_has_closed_by_the_time_the_truck_arrives_makes_the_deadline_impossible()
    {
        var o = Forward("Aaa");

        var plan = await Optimizer().OptimizeAsync(Input([At(o, 20, 21, by: Today, from: new TimeOnly(7, 0), to: new TimeOnly(8, 30))]), default); // arrives 09:51

        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.DeadlineImpossible);
    }

    [Fact]
    public async Task Orders_are_split_when_travelling_together_would_cost_more_than_separate_trips()
    {
        // Together they weigh 5,000 kg: only the 32ft (30,000) carries them. Separately each fits a 14ft (10,000 each = 20,000).
        var a = Forward("Aaa", 2_500m);
        var b = Forward("Bbb", 2_500m);

        var plan = await Optimizer().OptimizeAsync(Input([At(a, 20, 21), At(b, 20, 22)]), default);

        plan.Vehicles.Count.ShouldBe(2);
        plan.Summary.TotalCost.ShouldBe(20_000m);
        plan.Vehicles.ShouldAllBe(v => v.VehicleTypeName == "14ft" && v.RouteNote != null && v.RouteNote.Contains("separate trips"));
    }

    [Fact]
    public async Task A_consolidated_trip_reports_the_saving_percentage_and_the_extra_distance()
    {
        var a = Forward("Aaa", 1_500m);
        var b = Forward("Bbb", 1_500m);

        var vehicle = (await Optimizer().OptimizeAsync(Input([At(a, 20, 22), At(b, 20, 23)]), default)).Vehicles.ShouldHaveSingleItem();

        vehicle.Orders.Count.ShouldBe(2);
        vehicle.SeparateCost.ShouldBe(20_000m);
        vehicle.ConsolidationSaving.ShouldBe(10_000m);
        vehicle.SavingPercent.ShouldBe(50m);
        vehicle.AdditionalKm.ShouldBe(0); // the nearer drop lies on the way to the farther one
        vehicle.AdditionalMinutes.ShouldNotBeNull();
    }

    [Fact]
    public async Task Orders_that_cannot_ride_together_are_planned_separately_instead_of_being_lost()
    {
        // Together they would need a rate nobody has (weight over 4,000 kg), but each alone is priced.
        var a = Forward("Aaa", 2_500m);
        var b = Forward("Bbb", 2_500m);
        var optimizer = Optimizer(r => r.WeightKg > 4_000m ? null : Standard(r));

        var plan = await optimizer.OptimizeAsync(Input([At(a, 20, 21), At(b, 20, 22)]), default);

        plan.Vehicles.Count.ShouldBe(2);
        plan.Unplanned.ShouldBeEmpty();
    }

    // ---- return pickups

    private static Order Return(decimal kg, string pickupCity = "Midtown") =>
        NewOrder(kg, 2m, pickup: Customer("Customer " + pickupCity, pickupCity, "Maharashtra"), drop: Plant, direction: OrderDirection.Reverse);

    [Fact]
    public async Task A_return_pickup_is_added_on_the_way_back_when_it_fits_and_the_saving_is_shown()
    {
        var forward = Forward("Aaa", 3_000m);
        var back = Return(1_000m);
        var orders = new[] { At(forward, 20, 22), At(back, 21.5, 20) };

        var plan = await Optimizer().OptimizeAsync(Input(orders), default);

        var vehicle = plan.Vehicles.ShouldHaveSingleItem();
        vehicle.Orders.Select(o => (o.Number, o.Kind)).ToArray().ShouldBe([(forward.Number, "Delivery"), (back.Number, "ReturnPickup")]);
        vehicle.EstimatedCost.ShouldBe(15_000m); // 10,000 forward + 50% of a standalone 10,000 return trip
        vehicle.BackhaulSaving.ShouldBe(5_000m);
        vehicle.Stops!.Select(s => s.Kind).ToArray().ShouldBe(["Pickup", "Drop", "ReturnPickup", "Return"]);
        vehicle.DistanceKm.ShouldBe(444); // out 222, back via the pickup 222: on the way
        vehicle.RouteNote.ShouldNotBeNull().ShouldContain("return");
        plan.Summary.ReturnPickups.ShouldBe(1);
        plan.Unplanned.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_return_that_is_too_heavy_for_the_vehicle_is_not_attached_and_gets_its_own_vehicle()
    {
        var forward = Forward("Aaa", 3_000m);
        var back = Return(5_000m); // more than the 14ft carries
        var orders = new[] { At(forward, 20, 22), At(back, 21.5, 20) };

        var plan = await Optimizer().OptimizeAsync(Input(orders), default);

        plan.Vehicles.Count.ShouldBe(2);
        plan.Vehicles.SelectMany(v => v.Orders).ShouldNotContain(o => o.Kind == "ReturnPickup");
    }

    [Fact]
    public async Task A_return_far_off_the_route_is_refused_because_the_detour_is_too_long()
    {
        var forward = Forward("Aaa", 3_000m);
        var back = Return(500m, "Faraway");
        var orders = new[] { At(forward, 20, 22), At(back, 30, 20) }; // 10 degrees north: a ~2,000 km detour

        var plan = await Optimizer().OptimizeAsync(Input(orders), default);

        plan.Vehicles.SelectMany(v => v.Orders).ShouldNotContain(o => o.Kind == "ReturnPickup");
        plan.Vehicles.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_return_whose_deadline_the_trip_back_would_miss_is_not_attached()
    {
        var forward = Forward("Aaa", 3_000m);
        var back = Return(500m);
        var orders = new[] { At(forward, 20, 22), At(back, 21.5, 20, by: Today) }; // back at the depot ~15:40 next... still same day end 24:00, tighten below

        var tight = await Optimizer().OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false, StopServiceMinutes: 600)), default); // 10 h at each stop

        tight.Vehicles.SelectMany(v => v.Orders).ShouldNotContain(o => o.Kind == "ReturnPickup");
    }

    [Fact]
    public async Task Returns_can_be_switched_off()
    {
        var forward = Forward("Aaa", 3_000m);
        var back = Return(1_000m);

        var plan = await Optimizer().OptimizeAsync(Input([At(forward, 20, 22), At(back, 21.5, 20)], new PlanOptions(AllowPtl: false, AllowBackhaul: false)), default);

        plan.Vehicles.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_return_with_no_rate_for_its_own_lane_is_left_to_the_standalone_path_which_says_why()
    {
        var forward = Forward("Aaa", 3_000m);
        var back = Return(1_000m);
        var optimizer = Optimizer(r => string.Equals(r.OriginState, "Maharashtra", StringComparison.OrdinalIgnoreCase) && string.Equals(r.DestinationState, "Maharashtra", StringComparison.OrdinalIgnoreCase) ? null : Standard(r));

        var plan = await optimizer.OptimizeAsync(Input([At(forward, 20, 22), At(back, 21.5, 20)]), default);

        plan.Vehicles.ShouldHaveSingleItem().Orders.ShouldNotContain(o => o.Kind == "ReturnPickup");
        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.NoRate);
    }
}
