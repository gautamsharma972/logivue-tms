using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class MilkRunPlannerTests
{
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
        public List<FreightQuoteRequest> Requests { get; } = [];

        public Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var total = price(request);
            IReadOnlyList<FreightQuoteResult> quotes = total is null ? [] : [new FreightQuoteResult(Guid.NewGuid(), "CN-1", Transporter, "Shree", FreightMode.Ftl, "lane", null, [new FreightQuoteLine("FREIGHT", "Freight", total.Value)], [], total.Value)];
            return Task.FromResult(new FreightQuoteSet(quotes, null));
        }
    }

    private static decimal? Standard(FreightQuoteRequest r) => r.VehicleTypeId == Truck14.Id ? 10_000m : r.VehicleTypeId == Truck32.Id ? 25_000m : 4_000m;

    private static MilkRunStopDef Stop(string name, double lat, MilkRunStopType type = MilkRunStopType.Pickup, int service = 30, TimeOnly? from = null, TimeOnly? to = null) =>
        new(Guid.NewGuid(), name, name, "Maharashtra", new GeoPoint(lat, 74), type, service, from, to);

    private static MilkRunDefinition Template(MilkRunStopDef[] stops, Guid? preferred = null, int maxStops = 8, int maxMinutes = 1440) =>
        new("MR-1", "Pune supplier run", "Pune DC", "Pune", "Maharashtra", new GeoPoint(19, 74), preferred, maxStops, maxMinutes, new TimeOnly(7, 0), stops);

    private static MilkRunOrder Order(int stop, decimal kg, decimal? volume = 5m, DateOnly? by = null) =>
        new(Guid.NewGuid(), $"ORD-{Guid.NewGuid().ToString("N")[..5]}", stop, kg, volume, by);

    private static MilkRunInput Input(MilkRunDefinition t, IEnumerable<MilkRunOrder> orders, MilkRunOptions? options = null) =>
        new(Today, t, orders.ToList(), Types, options ?? new MilkRunOptions());

    private static MilkRunPlanner Planner(Func<FreightQuoteRequest, decimal?>? price = null) => new(new Prices(price ?? Standard), new MeridianRouting());

    [Fact]
    public async Task Stops_with_nothing_today_are_skipped_and_the_route_shortens()
    {
        var t = Template([Stop("S1", 19.5), Stop("S2", 20), Stop("S3", 20.5)]);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 1_000), Order(2, 1_500)]), default);

        plan.Skipped.ShouldHaveSingleItem().Label.ShouldBe("S2");
        var trip = plan.Trips.ShouldHaveSingleItem();
        trip.Stops.Select(s => s.Label).ToArray().ShouldBe(["S1", "S3", "Pune DC"]);
        trip.DistanceKm.ShouldBe(333); // out to 20.5 and back
        trip.PeakWeightKg.ShouldBe(2_500m);
        plan.Totals.StopsServed.ShouldBe(2);
        plan.Totals.StopsSkipped.ShouldBe(1);
        plan.Totals.InboundKg.ShouldBe(2_500m);
    }

    [Fact]
    public async Task The_vehicle_follows_the_days_load_not_the_usual_choice()
    {
        var t = Template([Stop("S1", 19.5)], preferred: Truck32.Id);

        var light = await Planner().PlanAsync(Input(t, [Order(0, 1_000)]), default);
        var heavy = await Planner().PlanAsync(Input(t, [Order(0, 9_000)]), default);

        light.Trips.Single().VehicleTypeName.ShouldBe("14ft");
        light.Trips.Single().Reason.ShouldContain("usual vehicle was not used");
        heavy.Trips.Single().VehicleTypeName.ShouldBe("32ft"); // 9,000 kg does not fit a 14ft
        heavy.Trips.Single().Cost.ShouldBe(25_000m);
    }

    [Fact]
    public async Task The_usual_vehicle_wins_a_tie()
    {
        var t = Template([Stop("S1", 19.5)], preferred: Truck32.Id);

        var plan = await Planner(r => 10_000m).PlanAsync(Input(t, [Order(0, 1_000)]), default);

        plan.Trips.Single().VehicleTypeName.ShouldBe("32ft");
    }

    [Fact]
    public async Task A_day_too_big_for_one_vehicle_is_split_into_trips()
    {
        var t = Template([Stop("S1", 19.5), Stop("S2", 20), Stop("S3", 20.5)]);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 9_000), Order(1, 9_000), Order(2, 9_000)]), default); // largest carries 16,000 kg

        plan.Trips.Count.ShouldBe(3);
        plan.Trips.ShouldAllBe(x => x.PeakWeightKg <= 16_000m);
        plan.Unplanned.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_stop_limit_splits_a_busy_day()
    {
        var t = Template([Stop("S1", 19.5), Stop("S2", 20), Stop("S3", 20.5)], maxStops: 2);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 500), Order(1, 500), Order(2, 500)]), default);

        plan.Trips.Select(x => x.Stops.Count(s => s.Kind != "Depot")).ToArray().ShouldBe([2, 1]);
    }

    [Fact]
    public async Task The_duration_limit_splits_a_long_day_into_trips_that_each_fit()
    {
        var t = Template([Stop("S1", 19.5), Stop("S2", 20), Stop("S3", 20.5), Stop("S4", 21)], maxMinutes: 500);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 500), Order(1, 500), Order(2, 500), Order(3, 500)]), default);

        plan.Trips.Count.ShouldBeGreaterThan(1);
        plan.Trips.ShouldAllBe(x => x.TotalMinutes <= 500);
        plan.Trips.Sum(x => x.Stops.Count(s => s.Kind != "Depot")).ShouldBe(4);
    }

    [Fact]
    public async Task The_planned_order_is_kept_by_default_and_the_shortest_order_is_reported_beside_it()
    {
        var t = Template([Stop("Far", 22), Stop("Near", 20), Stop("Mid", 21)]); // planned order is far, near, mid
        var orders = new[] { Order(0, 500), Order(1, 500), Order(2, 500) };

        var kept = (await Planner().PlanAsync(Input(t, orders), default)).Trips.Single();
        var shortest = (await Planner().PlanAsync(Input(t, orders, new MilkRunOptions(KeepTemplateOrder: false)), default)).Trips.Single();

        kept.Stops.Select(s => s.Label).Take(3).ToArray().ShouldBe(["Far", "Near", "Mid"]);
        kept.TemplateOrderKm.ShouldBe(888);
        kept.ShortestOrderKm.ShouldBe(666); // the hint: re-sequencing would save 222 km
        kept.DistanceKm.ShouldBe(888);
        shortest.DistanceKm.ShouldBe(666);
        var order = shortest.Stops.Select(s => s.Label).Take(3).ToArray();
        (order.SequenceEqual(["Near", "Mid", "Far"]) || order.SequenceEqual(["Far", "Mid", "Near"])).ShouldBeTrue(); // out and back is equally short either way round
    }

    [Fact]
    public async Task A_mixed_run_delivers_first_then_collects_and_checks_the_load_at_each_point()
    {
        var t = Template([Stop("Supplier", 19.5), Stop("Store", 20, MilkRunStopType.Delivery)]);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 6_000), Order(1, 5_000)]), default); // planned order collects first

        var trip = plan.Trips.ShouldHaveSingleItem();
        trip.Stops.Select(s => s.Kind).ToArray().ShouldBe(["Delivery", "Pickup", "Depot"]); // the planned order breaks "deliver first", so it is re-sequenced
        trip.Warnings.ShouldContain(w => w.Contains("re-sequenced"));
        trip.PeakWeightKg.ShouldBe(6_000m);
        plan.Totals.OutboundKg.ShouldBe(5_000m);
        plan.Totals.InboundKg.ShouldBe(6_000m);
    }

    [Fact]
    public async Task A_stop_window_makes_the_truck_wait_and_shows_it()
    {
        var t = Template([Stop("S1", 19.2, from: new TimeOnly(10, 0), to: new TimeOnly(15, 0))]);

        var trip = (await Planner().PlanAsync(Input(t, [Order(0, 500)]), default)).Trips.Single();

        trip.Stops[0].WaitMinutes.ShouldNotBeNull().ShouldBeGreaterThan(0); // reaches the supplier before 10:00
        trip.Stops[0].Departure!.Value.Hour.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task A_delivery_that_cannot_arrive_before_its_deadline_is_unplanned_with_the_reason()
    {
        var t = Template([Stop("Store", 30, MilkRunStopType.Delivery)]);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 500, by: Today)]), default); // ~1,200 km: cannot arrive the same day

        plan.Trips.ShouldBeEmpty();
        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.DeadlineImpossible);
    }

    [Fact]
    public async Task A_run_with_no_contract_rate_is_still_planned_with_an_unknown_cost_and_a_warning()
    {
        var t = Template([Stop("S1", 19.5)]);

        var plan = await Planner(_ => null).PlanAsync(Input(t, [Order(0, 500)]), default);

        var trip = plan.Trips.ShouldHaveSingleItem();
        trip.Cost.ShouldBeNull();
        trip.Warnings.ShouldContain(w => w.Contains("No active contract"));
        plan.Totals.Cost.ShouldBeNull();
    }

    [Fact]
    public async Task One_stop_heavier_than_any_vehicle_is_unplanned_and_the_rest_still_run()
    {
        var t = Template([Stop("Huge", 19.5), Stop("Small", 20)]);

        var plan = await Planner().PlanAsync(Input(t, [Order(0, 20_000), Order(1, 500)]), default);

        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.PayloadExceeded);
        plan.Trips.ShouldHaveSingleItem().Stops[0].Label.ShouldBe("Small");
    }

    [Fact]
    public async Task A_day_with_no_orders_has_no_trips_and_every_stop_skipped()
    {
        var plan = await Planner().PlanAsync(Input(Template([Stop("S1", 19.5), Stop("S2", 20)]), []), default);

        plan.Trips.ShouldBeEmpty();
        plan.Skipped.Count.ShouldBe(2);
        plan.Totals.Orders.ShouldBe(0);
    }

    [Fact]
    public async Task The_lane_is_priced_to_the_farthest_stop_with_the_route_distance_and_stop_count()
    {
        var prices = new Prices(Standard);
        var t = Template([Stop("Near", 19.5), Stop("Far", 21)]);

        await new MilkRunPlanner(prices, new MeridianRouting()).PlanAsync(Input(t, [Order(0, 500), Order(1, 500)]), default);

        var request = prices.Requests[0];
        request.OriginCity.ShouldBe("Far"); // collecting: the goods travel from the farthest stop to the depot
        request.DestinationCity.ShouldBe("Pune");
        request.Drops.ShouldBe(2);
        request.DistanceKm.ShouldBe(444m); // 19 → 19.5 → 21 → 19
    }

    [Fact]
    public async Task A_delivery_run_is_priced_from_the_depot_to_the_farthest_stop()
    {
        var prices = new Prices(Standard);
        var t = Template([Stop("Near", 19.5, MilkRunStopType.Delivery), Stop("Far", 21, MilkRunStopType.Delivery)]);

        await new MilkRunPlanner(prices, new MeridianRouting()).PlanAsync(Input(t, [Order(0, 500), Order(1, 500)]), default);

        prices.Requests[0].OriginCity.ShouldBe("Pune");
        prices.Requests[0].DestinationCity.ShouldBe("Far");
    }

    [Fact]
    public async Task The_plan_reports_where_its_distances_came_from()
    {
        var plan = await Planner().PlanAsync(Input(Template([Stop("S1", 19.5)]), [Order(0, 500)]), default);

        plan.Source.ShouldBe(RouteSource.Osrm);
    }

    private sealed class Fleet(IReadOnlyList<FleetVehicle> vehicles, IReadOnlyList<FleetDriver> drivers) : IFleetDirectory, ITransporterDirectory
    {
        public Task<FleetVehicle?> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default) => Task.FromResult(vehicles.FirstOrDefault(v => v.Id == vehicleId));

        public Task<FleetDriver?> GetDriverAsync(Guid driverId, CancellationToken cancellationToken = default) => Task.FromResult(drivers.FirstOrDefault(d => d.Id == driverId));

        public Task<IReadOnlyList<FleetVehicle>> ListVehiclesAsync(Guid transporterId, CancellationToken cancellationToken = default) => Task.FromResult(vehicles);

        public Task<IReadOnlyList<FleetDriver>> ListDriversAsync(Guid transporterId, CancellationToken cancellationToken = default) => Task.FromResult(drivers);

        public Task<bool> ExistsAsync(Guid transporterId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyDictionary<Guid, TransporterInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, TransporterInfo>>(ids.ToDictionary(id => id, id => new TransporterInfo(id, "SHR", "Shree Roadlines", true, "Anil Kumar", "9800000001", "ops@shree.example", "Pune")));
    }

    private static FleetVehicle Truck(string plate, Guid typeId, string typeName, int payload) =>
        new(Guid.NewGuid(), Transporter, plate, typeId, typeName, payload, true, FleetCompliance.Compliant, []);

    [Fact]
    public async Task A_trip_names_the_transporter_the_vehicle_and_the_driver()
    {
        var fleet = new Fleet(
            [Truck("MH12AB0001", Truck14.Id, "14ft", 4_000), Truck("MH12AB0002", Truck32.Id, "32ft", 16_000)],
            [new FleetDriver(Guid.NewGuid(), Transporter, "Ramesh Yadav", "9876543210", "MH1220190001234", true, FleetCompliance.Compliant, [])]);
        var planner = new MilkRunPlanner(new Prices(Standard), new MeridianRouting(), fleet, fleet);

        var plan = await planner.PlanAsync(Input(Template([Stop("S1", 19.5)]), [Order(0, 1_000)]), default);

        var trip = plan.Trips.ShouldHaveSingleItem();
        trip.Transporter!.Name.ShouldBe("Shree Roadlines");
        trip.Transporter.Phone.ShouldBe("9800000001");
        trip.Vehicle!.Registration.ShouldBe("MH12AB0001");
        trip.Driver!.Name.ShouldBe("Ramesh Yadav");
        trip.Alternatives.Single(a => a.Chosen).Vehicle!.Registration.ShouldBe("MH12AB0001");
    }

    [Fact]
    public async Task Two_trips_on_one_day_never_share_a_vehicle_or_a_driver()
    {
        var fleet = new Fleet(
            [Truck("MH12AB0001", Truck14.Id, "14ft", 4_000), Truck("MH12AB0002", Truck14.Id, "14ft", 4_000)],
            [new FleetDriver(Guid.NewGuid(), Transporter, "Ramesh", "9876543210", "L1", true, FleetCompliance.Compliant, []),
             new FleetDriver(Guid.NewGuid(), Transporter, "Suresh", "9876543211", "L2", true, FleetCompliance.Compliant, [])]);
        var planner = new MilkRunPlanner(new Prices(Standard), new MeridianRouting(), fleet, fleet);

        var plan = await planner.PlanAsync(Input(Template([Stop("S1", 19.5), Stop("S2", 20)], maxStops: 1), [Order(0, 1_000), Order(1, 1_000)]), default);

        plan.Trips.Count.ShouldBe(2);
        plan.Trips.Select(t => t.Vehicle!.Id).Distinct().Count().ShouldBe(2);
        plan.Trips.Select(t => t.Driver!.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_run_with_no_free_vehicle_is_unplanned_with_the_reason()
    {
        var fleet = new Fleet([Truck("MH12AB0001", Truck14.Id, "14ft", 4_000) with { Availability = FleetAvailability.InMaintenance }], []);
        var planner = new MilkRunPlanner(new Prices(Standard), new MeridianRouting(), fleet, fleet);

        var plan = await planner.PlanAsync(Input(Template([Stop("S1", 19.5)]), [Order(0, 1_000)]), default);

        plan.Trips.ShouldBeEmpty();
        plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.NoAvailableVehicle);
    }
}
