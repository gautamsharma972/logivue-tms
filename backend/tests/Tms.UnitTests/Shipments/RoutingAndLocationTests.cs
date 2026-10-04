using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Routing;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class LocationTests
{
    private static Tms.SharedKernel.Results.Result<Location> Make(double lat = 18.5204, double lon = 73.8567, string code = "pune-dc", string pin = "411019") =>
        Location.Create(Tenant, code, "Pune DC", LocationType.Depot, "MIDC Phase 2", "Pune", "Maharashtra", pin, lat, lon);

    [Fact]
    public void A_valid_location_is_stored_with_an_upper_case_code()
    {
        var location = Make().Value;

        location.Code.ShouldBe("PUNE-DC");
        location.IsActive.ShouldBeTrue();
        location.Point.IsInIndia.ShouldBeTrue();
    }

    [Fact]
    public void Swapped_coordinates_are_caught_because_they_fall_outside_india()
    {
        var result = Make(lat: 73.8567, lon: 18.5204);

        result.IsFailure.ShouldBeTrue();
        result.Error.ValidationErrors!.ShouldContainKey("latitude");
    }

    [Fact]
    public void Bad_pincode_and_code_characters_are_reported_by_field()
    {
        var result = Make(code: "bad code!", pin: "12");

        result.Error.ValidationErrors!.Keys.ShouldBe(["code", "pincode"], ignoreOrder: true);
    }
}

public class RoutingTests
{
    private static readonly GeoPoint Pune = new(18.5204, 73.8567);
    private static readonly GeoPoint Mumbai = new(19.0760, 72.8777);

    [Fact]
    public void Straight_line_distance_between_pune_and_mumbai_is_about_120_km()
    {
        Pune.StraightLineKm(Mumbai).ShouldBe(120, 5);
    }

    [Fact]
    public async Task The_estimate_applies_the_road_factor_and_is_labelled_an_estimate()
    {
        var provider = new EstimatedRoutingProvider(Options.Create(new RoutingOptions { CircuityFactor = 1.5, AverageSpeedKmh = 60 }));

        var route = await provider.GetRouteAsync([Pune, Mumbai], default);

        route.Source.ShouldBe(RouteSource.Estimate);
        route.TotalKm.ShouldBe(Pune.StraightLineKm(Mumbai) * 1.5, 0.1);
        route.TotalMinutes.ShouldBe(route.TotalKm, 0.5); // 60 km/h → one minute per km
    }

    [Fact]
    public void An_osrm_response_is_read_in_kilometres_and_minutes()
    {
        const string json = """{"code":"Ok","routes":[{"legs":[{"distance":148200.5,"duration":10800},{"distance":50000,"duration":3000}]}]}""";

        var route = OsrmRoutingProvider.Parse(json, 2);

        route.Source.ShouldBe(RouteSource.Osrm);
        route.Legs.Select(l => l.DistanceKm).ToArray().ShouldBe([148.2, 50]);
        route.Legs[0].DurationMinutes.ShouldBe(180);
    }

    [Fact]
    public void An_osrm_response_with_no_route_or_the_wrong_leg_count_is_rejected()
    {
        Should.Throw<InvalidOperationException>(() => OsrmRoutingProvider.Parse("""{"code":"NoRoute","routes":[]}""", 1));
        Should.Throw<InvalidOperationException>(() => OsrmRoutingProvider.Parse("""{"code":"Ok","routes":[{"legs":[{"distance":1,"duration":1}]}]}""", 2));
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static (ResilientRoutingProvider Provider, StubHandler Handler) Resilient(Func<HttpResponseMessage> respond, string? baseUrl = "http://osrm.local")
    {
        var options = Options.Create(new RoutingOptions { OsrmBaseUrl = baseUrl });
        var handler = new StubHandler(respond);
        var services = new FakeServices(new OsrmRoutingProvider(new HttpClient(handler), options));
        return (new ResilientRoutingProvider(options, services, new EstimatedRoutingProvider(options), new MemoryCache(new MemoryCacheOptions()), NullLogger<ResilientRoutingProvider>.Instance), handler);
    }

    private sealed class FakeServices(object osrm) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(OsrmRoutingProvider) ? osrm : null;
    }

    [Fact]
    public async Task With_osrm_configured_road_distance_is_used_and_cached()
    {
        var (provider, handler) = Resilient(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"code":"Ok","routes":[{"legs":[{"distance":150000,"duration":9000}]}]}""") });

        var first = await provider.GetRouteAsync([Pune, Mumbai], default);
        var second = await provider.GetRouteAsync([Pune, Mumbai], default);

        first.Source.ShouldBe(RouteSource.Osrm);
        first.TotalKm.ShouldBe(150);
        second.TotalKm.ShouldBe(150);
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task When_osrm_is_down_the_estimate_is_used_and_says_so()
    {
        var (provider, _) = Resilient(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var route = await provider.GetRouteAsync([Pune, Mumbai], default);

        route.Source.ShouldBe(RouteSource.Estimate);
        route.TotalKm.ShouldBeGreaterThan(100);
    }

    [Fact]
    public async Task Without_an_osrm_url_the_estimate_is_used_and_osrm_is_never_called()
    {
        var (provider, handler) = Resilient(() => new HttpResponseMessage(HttpStatusCode.OK), baseUrl: null);

        (await provider.GetRouteAsync([Pune, Mumbai], default)).Source.ShouldBe(RouteSource.Estimate);
        handler.Calls.ShouldBe(0);
    }
}

public class RoutedPlanningTests
{
    private sealed class FixedRoute(double kmPerLeg, double minutesPerLeg) : IRoutingProvider
    {
        public Task<RouteResult> GetRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken) =>
            Task.FromResult(new RouteResult(Enumerable.Range(1, waypoints.Count - 1).Select(_ => new RouteLeg(kmPerLeg, minutesPerLeg)).ToList(), RouteSource.Osrm));

        /// <summary>Every pair of distinct points is the same distance apart.</summary>
        public Task<DistanceMatrix> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken)
        {
            var km = new double[points.Count, points.Count];
            var min = new double[points.Count, points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                for (var j = 0; j < points.Count; j++)
                {
                    km[i, j] = i == j ? 0 : kmPerLeg;
                    min[i, j] = i == j ? 0 : minutesPerLeg;
                }
            }

            return Task.FromResult(new DistanceMatrix(km, min, RouteSource.Osrm));
        }
    }

    private sealed class CapturingQuotes(Func<FreightQuoteRequest, decimal?> price) : IFreightQuoteService
    {
        public List<decimal?> Distances { get; } = [];

        public Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            Distances.Add(request.DistanceKm);
            var total = price(request);
            IReadOnlyList<FreightQuoteResult> quotes = total is null ? [] : [new FreightQuoteResult(Guid.NewGuid(), "CN-1", Transporter, "Shree", request.Mode ?? FreightMode.Ftl, "lane", null, [new FreightQuoteLine("FREIGHT", "Freight", total.Value)], [], total.Value)];
            return Task.FromResult(new FreightQuoteSet(quotes, null));
        }
    }

    private static readonly GeoPoint PickupPoint = new(18.5204, 73.8567);
    private static readonly GeoPoint DropPoint = new(21.1702, 72.8311);

    private static PlannableOrder Routed(Order o, DateOnly? by = null) =>
        new(o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, by, PickupPoint, DropPoint);

    private static PlanningInput Input(IEnumerable<PlannableOrder> orders, PlanOptions options) => new(Today, orders.ToList(), Types, options, []);

    private static decimal? Price(FreightQuoteRequest r) => r.Mode == FreightMode.Ptl ? r.WeightKg * 5m : r.VehicleTypeId == Truck14.Id ? 10_000m : 30_000m;

    [Fact]
    public async Task The_road_distance_is_passed_to_pricing_and_shown_on_the_plan_with_its_source()
    {
        var quotes = new CapturingQuotes(Price);
        var optimizer = new RuleBasedPlanningOptimizer(quotes, new FixedRoute(300, 360));

        var plan = await optimizer.OptimizeAsync(Input([Routed(NewOrder(3_000m, 10m))], new PlanOptions(AllowPtl: false)), default);

        quotes.Distances.ShouldAllBe(d => d == 300m);
        var vehicle = plan.Vehicles.Single();
        vehicle.DistanceKm.ShouldBe(300);
        vehicle.DurationMinutes.ShouldBe(360);
        vehicle.RouteSource.ShouldBe(RouteSource.Osrm);
        vehicle.CostPerTonneKm.ShouldBe(11.11m); // 10,000 / (3 t × 300 km)
        plan.Summary.TotalDistanceKm.ShouldBe(300);
    }

    [Fact]
    public async Task Stops_carry_estimated_arrival_and_departure_times()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new CapturingQuotes(Price), new FixedRoute(300, 360));

        var vehicle = (await optimizer.OptimizeAsync(Input([Routed(NewOrder(3_000m, 10m))], new PlanOptions(AllowPtl: false)), default)).Vehicles.Single();

        vehicle.Stops.ShouldNotBeNull().Count.ShouldBe(2);
        vehicle.Stops![0].Kind.ShouldBe("Pickup");
        vehicle.PlannedDeparture!.Value.Hour.ShouldBe(8);
        vehicle.Stops[1].PlannedArrival.ShouldBe(vehicle.PlannedDeparture!.Value.AddHours(6));
        vehicle.TransitHours.ShouldBe(6.5); // 6 h driving + 30 min at the drop
    }

    [Fact]
    public async Task A_cheaper_part_load_that_would_miss_the_deadline_is_rejected_in_favour_of_a_full_truck()
    {
        var order = NewOrder(2_000m, 5m);
        var optimizer = new RuleBasedPlanningOptimizer(new CapturingQuotes(r => r.Mode == FreightMode.Ptl ? 2_000m : 10_000m), new FixedRoute(400, 480));

        var plan = await optimizer.OptimizeAsync(Input([Routed(order, by: Today)], new PlanOptions()), default);

        var vehicle = plan.Vehicles.Single();
        vehicle.Mode.ShouldBe(FreightMode.Ftl); // PTL: 8 h + 24 h extra, deadline end of day
        var ptl = vehicle.Alternatives.Single(a => a.Mode == FreightMode.Ptl);
        ptl.MeetsDeadline.ShouldBe(false);
        ptl.Verdict.ShouldContain("deadline");
    }

    [Fact]
    public async Task Part_load_is_chosen_when_deadlines_are_not_enforced()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new CapturingQuotes(r => r.Mode == FreightMode.Ptl ? 2_000m : 10_000m), new FixedRoute(400, 480));

        var plan = await optimizer.OptimizeAsync(Input([Routed(NewOrder(2_000m, 5m), by: Today)], new PlanOptions(EnforceDeadlines: false)), default);

        plan.Vehicles.Single().Mode.ShouldBe(FreightMode.Ptl);
    }

    [Fact]
    public async Task When_nothing_can_arrive_in_time_the_order_is_unplanned_with_the_earliest_possible_arrival()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new CapturingQuotes(Price), new FixedRoute(2_000, 2_400));

        var plan = await optimizer.OptimizeAsync(Input([Routed(NewOrder(2_000m, 5m), by: Today)], new PlanOptions()), default);

        plan.Vehicles.ShouldBeEmpty();
        var unplanned = plan.Unplanned.ShouldHaveSingleItem();
        unplanned.Code.ShouldBe(UnplannedCodes.DeadlineImpossible);
        unplanned.Reason.ShouldContain("deadline");
        unplanned.Suggestions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Orders_without_coordinates_are_still_planned_but_have_no_distance_or_sla_check()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new CapturingQuotes(Price), new FixedRoute(300, 360));
        var plain = NewOrder(3_000m, 10m);
        var order = new PlannableOrder(plain.Id, plain.Number, plain.Direction, plain.PickupState, plain.PickupCity, plain.DropState, plain.DropCity, plain.WeightKg, plain.VolumeCbm, plain.ReadyDate, Today);

        var plan = await optimizer.OptimizeAsync(Input([order], new PlanOptions(AllowPtl: false)), default);

        var vehicle = plan.Vehicles.ShouldHaveSingleItem();
        vehicle.DistanceKm.ShouldBeNull();
        vehicle.Stops.ShouldBeNull();
    }

    [Fact]
    public async Task A_multi_drop_route_visits_every_drop_with_a_leg_for_each()
    {
        var optimizer = new RuleBasedPlanningOptimizer(new CapturingQuotes(Price), new FixedRoute(100, 120));
        var orders = new[] { Routed(NewOrder(1_000m, 3m)), Routed(NewOrder(1_000m, 3m)) };

        var vehicle = (await optimizer.OptimizeAsync(Input(orders, new PlanOptions(AllowPtl: false)), default)).Vehicles.Single();

        vehicle.DistanceKm.ShouldBe(200); // pickup → drop 1 → drop 2
        var stops = vehicle.Stops.ShouldNotBeNull();
        stops.Select(s => s.Kind).ToArray().ShouldBe(["Pickup", "Drop", "Drop"]);
        stops[2].PlannedArrival.ShouldBe(vehicle.PlannedDeparture!.Value.AddMinutes(120 + 30 + 120));
    }
}
