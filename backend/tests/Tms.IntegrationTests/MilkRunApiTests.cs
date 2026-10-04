using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.Locations;
using Tms.Modules.Shipments.Application.MilkRuns;
using Tms.Modules.Shipments.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MilkRunApiTests(TmsApiFactory factory)
{
    private static readonly DayOfWeek[] AllDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static async Task<LocationDto> LocationAsync(HttpClient client, string state, string city, double lat, LocationType type)
    {
        var response = await client.PostJsonAsync("/api/v1/locations", new SaveLocationRequest($"L{Guid.NewGuid().ToString("N")[..8]}", $"{city} site", type, "Plot 1, Industrial Area", city, state, "411019", lat, 74.0));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LocationDto>();
    }

    private static SaveMilkRunRequest Template(LocationDto depot, params LocationDto[] stops) =>
        new($"MR{Guid.NewGuid().ToString("N")[..6]}", "Supplier collection", depot.Id, null, 8, 900, new TimeOnly(7, 0), AllDays,
            stops.Select(s => new MilkRunStopRequest(s.Id, MilkRunStopType.Pickup, 20)).ToList());

    private sealed class Setup
    {
        public required ShipmentScenario S { get; init; }

        public required LocationDto Depot { get; init; }

        public required LocationDto SupplierA { get; init; }

        public required LocationDto SupplierB { get; init; }

        public required LocationDto SupplierC { get; init; }
    }

    private async Task<Setup> SetupAsync()
    {
        var s = await ShipmentScenario.CreateAsync(factory);
        // Goods are collected from suppliers (in DropState) and brought to the depot (in OriginState): that lane needs a rate.
        var carrier = await ContractApiData.ActiveTransporterAsync(s.Admin);
        var type = await ContractApiData.VehicleTypeAsync(s.Admin);
        await ContractApiData.ActiveContractAsync(s.Admin, carrier.Id, ContractType.Ftl, null,
            ContractApiData.Flat(ContractApiData.State(s.DropState), ContractApiData.State(s.OriginState), 12_000m, type.Id));
        await ShipmentScenario.RegisterFleetAsync(s.Admin, carrier.Id, type.Id);

        return new Setup
        {
            S = s,
            Depot = await LocationAsync(s.Admin, s.OriginState, "Depot Town", 19.0, LocationType.Depot),
            SupplierA = await LocationAsync(s.Admin, s.DropState, "Supplier A", 19.5, LocationType.Supplier),
            SupplierB = await LocationAsync(s.Admin, s.DropState, "Supplier B", 20.0, LocationType.Supplier),
            SupplierC = await LocationAsync(s.Admin, s.DropState, "Supplier C", 20.5, LocationType.Supplier),
        };
    }

    private static async Task<OrderDto> InboundAsync(Setup x, LocationDto supplier, decimal kg)
    {
        var response = await x.S.Admin.PostJsonAsync("/api/v1/orders",
            x.S.NewOrder(kg, direction: OrderDirection.Reverse) with { PickupLocationId = supplier.Id, DropLocationId = x.Depot.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrderDto>();
    }

    [Fact]
    public async Task Templates_are_validated_unique_and_editable_with_their_stops_replaced()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var request = Template(setup.Depot, setup.SupplierA, setup.SupplierB);

        var created = await admin.PostJsonAsync("/api/v1/planning/milk-runs", request);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = await created.ReadAsync<MilkRunDto>();
        dto.Stops.Select(s => s.Sequence).ToArray().ShouldBe([1, 2]);
        dto.Stops[0].LocationName.ShouldBe("Supplier A site");

        (await admin.PostJsonAsync("/api/v1/planning/milk-runs", request with { Code = request.Code.ToLowerInvariant() })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.Depot))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // the depot is not also a stop
        (await admin.PostJsonAsync("/api/v1/planning/milk-runs", request with { Code = "X-" + Guid.NewGuid().ToString("N")[..5], Stops = [] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync("/api/v1/planning/milk-runs", request with { Code = "Y-" + Guid.NewGuid().ToString("N")[..5], Days = [] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var edit = request with
        {
            Name = "Renamed",
            Stops = [new MilkRunStopRequest(setup.SupplierC.Id, MilkRunStopType.Pickup, 15), new MilkRunStopRequest(setup.SupplierA.Id, MilkRunStopType.Pickup, 25)],
            Version = dto.Version,
        };
        var updated = await admin.PutJsonAsync($"/api/v1/planning/milk-runs/{dto.Id}", edit);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var after = await updated.ReadAsync<MilkRunDto>();
        after.Name.ShouldBe("Renamed");
        after.Stops.Select(s => s.LocationId).ToArray().ShouldBe([setup.SupplierC.Id, setup.SupplierA.Id]);

        (await admin.PutJsonAsync($"/api/v1/planning/milk-runs/{dto.Id}", edit)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // stale version
    }

    [Fact]
    public async Task A_collection_day_is_committed_into_one_shipment_that_can_be_priced_and_not_committed_twice()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var template = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.SupplierA, setup.SupplierB, setup.SupplierC))).ReadAsync<MilkRunDto>();
        var a = await InboundAsync(setup, setup.SupplierA, 3_000m);
        var c = await InboundAsync(setup, setup.SupplierC, 4_000m);

        var response = await admin.PostJsonAsync("/api/v1/planning/milk-runs/commit", new CommitMilkRunRequest(template.Id, ContractApiData.Today));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<CommitMilkRunResultDto>();
        var trip = result.Shipments.ShouldHaveSingleItem();
        trip.IsCollectionRun.ShouldBeTrue();
        trip.Orders.ShouldBe(2);
        trip.PlannedCost.ShouldBe(12_000m);

        var shipment = await (await admin.GetAsync($"/api/v1/shipments/{trip.ShipmentId}")).ReadAsync<ShipmentDto>();
        shipment.Summary.OrderCount.ShouldBe(2);
        shipment.Summary.TotalWeightKg.ShouldBe(7_000m);
        shipment.Summary.Status.ShouldBe(ShipmentStatus.Draft);
        shipment.PlanReference.ShouldNotBeNull().ShouldStartWith(template.Code);
        shipment.PlannedCost.ShouldBe(12_000m);

        // It prices like any other shipment: from the farthest supplier to the depot.
        var quotes = await admin.GetAsync($"/api/v1/shipments/{trip.ShipmentId}/quotes");
        quotes.StatusCode.ShouldBe(HttpStatusCode.OK, await quotes.Content.ReadAsStringAsync());
        (await quotes.ReadAsync<ShipmentQuotesDto>()).Quotes.ShouldNotBeEmpty();

        // The orders are taken, so a second commit has nothing left.
        var again = await admin.PostJsonAsync("/api/v1/planning/milk-runs/commit", new CommitMilkRunRequest(template.Id, ContractApiData.Today));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("milk_runs.nothing_to_commit");
        (await (await admin.GetAsync($"/api/v1/orders/{a.Id}")).ReadAsync<OrderDto>()).Status.ShouldBe(OrderStatus.Planned);
        (await (await admin.GetAsync($"/api/v1/orders/{c.Id}")).ReadAsync<OrderDto>()).ShipmentId.ShouldBe(trip.ShipmentId);
    }

    [Fact]
    public async Task Committing_a_milk_run_needs_the_planning_permission()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var template = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.SupplierA))).ReadAsync<MilkRunDto>();
        await InboundAsync(setup, setup.SupplierA, 1_000m);
        using var reader = await factory.UserWithPermissionsAsync(admin, "shipments.read");

        (await reader.PostJsonAsync("/api/v1/planning/milk-runs/commit", new CommitMilkRunRequest(template.Id, ContractApiData.Today))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await setup.S.Vendor.PostJsonAsync("/api/v1/planning/milk-runs/commit", new CommitMilkRunRequest(template.Id, ContractApiData.Today))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Templates_are_staff_only_and_belong_to_one_tenant()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var dto = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.SupplierA))).ReadAsync<MilkRunDto>();
        using var reader = await factory.UserWithPermissionsAsync(admin, "shipments.read");
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);

        (await reader.GetAsync($"/api/v1/planning/milk-runs/{dto.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.SupplierB))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostJsonAsync("/api/v1/planning/milk-runs/preview", new PlanMilkRunRequest(dto.Id, ContractApiData.Today))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await setup.S.Vendor.GetAsync("/api/v1/planning/milk-runs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await acme.GetAsync($"/api/v1/planning/milk-runs/{dto.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.PostJsonAsync("/api/v1/planning/milk-runs/preview", new PlanMilkRunRequest(dto.Id, ContractApiData.Today))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_day_is_planned_from_the_orders_at_its_stops_with_empty_stops_skipped()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var template = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.SupplierA, setup.SupplierB, setup.SupplierC))).ReadAsync<MilkRunDto>();
        var a = await InboundAsync(setup, setup.SupplierA, 3_000m);
        var c = await InboundAsync(setup, setup.SupplierC, 4_000m);

        var response = await admin.PostJsonAsync("/api/v1/planning/milk-runs/preview", new PlanMilkRunRequest(template.Id, ContractApiData.Today));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var plan = await response.ReadAsync<MilkRunPlan>();
        var trip = plan.Trips.ShouldHaveSingleItem();
        trip.Stops.Select(st => st.Label).ToArray().ShouldBe(["Supplier A site", "Supplier C site", "Depot Town site"]);
        trip.Stops[0].Orders.Single().OrderId.ShouldBe(a.Id);
        trip.PeakWeightKg.ShouldBe(7_000m);
        trip.Cost.ShouldBe(12_000m);
        trip.DistanceKm.ShouldBeGreaterThan(100);
        plan.Skipped.ShouldHaveSingleItem().Label.ShouldBe("Supplier B site");
        plan.Totals.Orders.ShouldBe(2);
        plan.Totals.InboundKg.ShouldBe(7_000m);
        plan.Source.ShouldBe(RouteSource.Estimate);

        // Tomorrow's load is different: the same template recalculates.
        var bigger = await InboundAsync(setup, setup.SupplierB, 9_000m);
        var again = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs/preview", new PlanMilkRunRequest(template.Id, ContractApiData.Today))).ReadAsync<MilkRunPlan>();
        again.Trips.Single().PeakWeightKg.ShouldBe(16_000m);
        again.Skipped.ShouldBeEmpty();
        again.Totals.Orders.ShouldBe(3);
        (await admin.GetAsync($"/api/v1/orders/{bigger.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        c.Number.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Orders_that_are_not_open_or_not_ready_or_not_at_the_stops_are_left_out_and_unscheduled_days_warn()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var weekdays = AllDays.Where(d => d != ContractApiData.Today.DayOfWeek).ToArray();
        var template = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs", Template(setup.Depot, setup.SupplierA) with { Days = weekdays })).ReadAsync<MilkRunDto>();
        var open = await InboundAsync(setup, setup.SupplierA, 1_000m);
        var cancelled = await InboundAsync(setup, setup.SupplierA, 2_000m);
        await admin.PostJsonAsync($"/api/v1/orders/{cancelled.Id}/cancel", new ReasonRequest("not needed"));
        var future = await (await admin.PostJsonAsync("/api/v1/orders",
            setup.S.NewOrder(500m, direction: OrderDirection.Reverse) with { PickupLocationId = setup.SupplierA.Id, DropLocationId = setup.Depot.Id, ReadyDate = ContractApiData.Today.AddDays(5) })).ReadAsync<OrderDto>();
        var elsewhere = await admin.PostJsonAsync("/api/v1/orders", setup.S.NewOrder(700m, direction: OrderDirection.Reverse) with { PickupLocationId = setup.SupplierB.Id, DropLocationId = setup.Depot.Id });
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.Created);

        var plan = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs/preview", new PlanMilkRunRequest(template.Id, ContractApiData.Today))).ReadAsync<MilkRunPlan>();

        plan.Trips.Single().Stops[0].Orders.Select(o => o.OrderId).ToArray().ShouldBe([open.Id]);
        plan.Warnings.ShouldContain(w => w.Contains("not one of this run's scheduled days"));
        future.Number.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_switched_off_milk_run_cannot_be_planned()
    {
        var setup = await SetupAsync();
        using var admin = setup.S.Admin;
        var request = Template(setup.Depot, setup.SupplierA);
        var dto = await (await admin.PostJsonAsync("/api/v1/planning/milk-runs", request)).ReadAsync<MilkRunDto>();
        await admin.PutJsonAsync($"/api/v1/planning/milk-runs/{dto.Id}", request with { IsActive = false, Version = dto.Version });

        var response = await admin.PostJsonAsync("/api/v1/planning/milk-runs/preview", new PlanMilkRunRequest(dto.Id, ContractApiData.Today));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("milk_runs.inactive");
    }
}
