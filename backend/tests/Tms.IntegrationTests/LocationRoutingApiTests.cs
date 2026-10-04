using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.Locations;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LocationRoutingApiTests(TmsApiFactory factory)
{
    private static SaveLocationRequest NewLocation(string state, string city, double lat, double lon, string? code = null, LocationType type = LocationType.Depot) =>
        new(code ?? $"L{Guid.NewGuid().ToString("N")[..8]}", $"{city} site", type, "Plot 1, Industrial Area", city, state, "411019", lat, lon);

    private static async Task<LocationDto> CreateAsync(HttpClient client, SaveLocationRequest request)
    {
        var response = await client.PostJsonAsync("/api/v1/locations", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LocationDto>();
    }

    [Fact]
    public async Task Locations_are_validated_unique_per_code_and_editable_with_concurrency_protection()
    {
        using var admin = await factory.AdminAsync();
        var location = await CreateAsync(admin, NewLocation("Maharashtra", "Pune", 18.5204, 73.8567, code: $"pune-{Guid.NewGuid().ToString("N")[..5]}"));
        location.Code.ShouldBe(location.Code.ToUpperInvariant());

        var swapped = await admin.PostJsonAsync("/api/v1/locations", NewLocation("Maharashtra", "Pune", 73.8567, 18.5204));
        swapped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var duplicate = await admin.PostJsonAsync("/api/v1/locations", NewLocation("Gujarat", "Surat", 21.17, 72.83, code: location.Code.ToLowerInvariant()));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ProblemCodeAsync()).ShouldBe("locations.code_exists");

        var edit = NewLocation("Maharashtra", "Pune", 18.53, 73.86, code: location.Code) with { Version = location.Version };
        var updated = await admin.PutJsonAsync($"/api/v1/locations/{location.Id}", edit);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        (await admin.PutJsonAsync($"/api/v1/locations/{location.Id}", edit)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // stale version
    }

    [Fact]
    public async Task Locations_are_staff_only_and_belong_to_one_tenant()
    {
        using var admin = await factory.AdminAsync();
        var location = await CreateAsync(admin, NewLocation("Maharashtra", "Pune", 18.52, 73.85));
        using var reader = await factory.UserWithPermissionsAsync(admin, "shipments.read");
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);

        (await reader.GetAsync($"/api/v1/locations/{location.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostJsonAsync("/api/v1/locations", NewLocation("Goa", "Panaji", 15.49, 73.82))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.GetAsync("/api/v1/locations")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await acme.GetAsync($"/api/v1/locations/{location.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await acme.GetAsync("/api/v1/locations?pageSize=200")).ReadAsync<PagedResult<LocationDto>>()).Items.ShouldNotContain(l => l.Id == location.Id);
    }

    [Fact]
    public async Task The_distance_between_two_locations_is_returned_and_labelled_as_an_estimate_without_osrm()
    {
        using var admin = await factory.AdminAsync();
        var pune = await CreateAsync(admin, NewLocation("Maharashtra", "Pune", 18.5204, 73.8567));
        var mumbai = await CreateAsync(admin, NewLocation("Maharashtra", "Mumbai", 19.0760, 72.8777));

        var response = await admin.PostJsonAsync("/api/v1/locations/distance", new DistanceRequest(pune.Id, mumbai.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var distance = await response.ReadAsync<DistanceDto>();
        distance.Source.ShouldBe(RouteSource.Estimate);
        distance.DistanceKm.ShouldBeInRange(120, 190);
        distance.DurationMinutes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_order_linked_to_locations_takes_its_address_from_them_and_inactive_ones_are_refused()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var pickup = await CreateAsync(s.Admin, NewLocation(s.OriginState, "Origin Town", 18.52, 73.85, type: LocationType.Plant));
        var drop = await CreateAsync(s.Admin, NewLocation(s.DropState, "Drop Town", 21.17, 72.83, type: LocationType.Customer));

        var response = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder() with { PickupLocationId = pickup.Id, DropLocationId = drop.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var order = await response.ReadAsync<OrderDto>();
        order.Pickup.City.ShouldBe("Origin Town");
        order.Drop.State.ShouldBe(s.DropState);
        order.PickupLocationId.ShouldBe(pickup.Id);
        order.Pickup.ContactName.ShouldBe("Contact"); // contact details still come from the order

        await s.Admin.PutJsonAsync($"/api/v1/locations/{drop.Id}", NewLocation(s.DropState, "Drop Town", 21.17, 72.83, code: drop.Code) with { IsActive = false, Version = drop.Version });
        var refused = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder() with { DropLocationId = drop.Id });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.ProblemCodeAsync()).ShouldBe("orders.location_invalid");
    }

    [Fact]
    public async Task A_plan_for_orders_with_locations_carries_distance_stops_eta_and_cost_per_tonne_km()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var pickup = await CreateAsync(s.Admin, NewLocation(s.OriginState, "Origin Town", 18.5204, 73.8567, type: LocationType.Plant));
        var drop = await CreateAsync(s.Admin, NewLocation(s.DropState, "Drop Town", 21.1702, 72.8311, type: LocationType.Customer));
        var created = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(8_000m) with { PickupLocationId = pickup.Id, DropLocationId = drop.Id });
        var order = await created.ReadAsync<OrderDto>();

        var response = await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [order.Id], new PlanOptions(AllowPtl: false)));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var run = await response.ReadAsync<RunDto>();
        var vehicle = run.Plan.Vehicles.ShouldHaveSingleItem();
        vehicle.DistanceKm.ShouldNotBeNull().ShouldBeGreaterThan(250);
        vehicle.RouteSource.ShouldBe(RouteSource.Estimate);
        vehicle.CostPerTonneKm.ShouldNotBeNull().ShouldBeGreaterThan(0);
        vehicle.Stops.ShouldNotBeNull().Count.ShouldBe(2);
        vehicle.Stops![1].PlannedArrival.ShouldNotBeNull();
        run.Plan.Summary.TotalDistanceKm.ShouldBe(vehicle.DistanceKm);
        run.Plan.Summary.CostPerTonneKm.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_delivery_date_the_route_cannot_meet_leaves_the_order_unplanned_unless_deadlines_are_relaxed()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var pickup = await CreateAsync(s.Admin, NewLocation(s.OriginState, "Origin Town", 8.5241, 76.9366)); // Thiruvananthapuram
        var drop = await CreateAsync(s.Admin, NewLocation(s.DropState, "Drop Town", 32.7266, 74.8570)); // Jammu: ~3,000 km by road
        var created = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(2_000m) with { PickupLocationId = pickup.Id, DropLocationId = drop.Id, DeliverByDate = ContractApiData.Today });
        var order = await created.ReadAsync<OrderDto>();

        var strict = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [order.Id], new PlanOptions(AllowPtl: false)))).ReadAsync<RunDto>();
        strict.Plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.DeadlineImpossible);

        var relaxed = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [order.Id], new PlanOptions(AllowPtl: false, EnforceDeadlines: false)))).ReadAsync<RunDto>();
        relaxed.Plan.Vehicles.ShouldHaveSingleItem();
    }
}
