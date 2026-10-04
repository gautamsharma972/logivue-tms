using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.Locations;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class Stage2PlanningApiTests(TmsApiFactory factory)
{
    private static async Task<LocationDto> LocationAsync(HttpClient client, string state, string city, double lat, double lon, LocationType type)
    {
        var response = await client.PostJsonAsync("/api/v1/locations", new SaveLocationRequest($"L{Guid.NewGuid().ToString("N")[..8]}", $"{city} site", type, "Plot 1, Industrial Area", city, state, "411019", lat, lon));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LocationDto>();
    }

    [Fact]
    public async Task A_return_pickup_rides_the_forward_truck_commits_into_one_shipment_and_can_be_dispatched()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);

        // The return lane (origin state → origin state) needs its own rate: a second carrier with an FTL contract for it.
        var other = await ContractApiData.ActiveTransporterAsync(s.Admin);
        var type = await ContractApiData.VehicleTypeAsync(s.Admin);
        await ContractApiData.ActiveContractAsync(s.Admin, other.Id, ContractType.Ftl, null,
            ContractApiData.Flat(ContractApiData.State(s.OriginState), ContractApiData.State(s.OriginState), 12_000m, type.Id));

        var depot = await LocationAsync(s.Admin, s.OriginState, "Origin Town", 18.50, 73.85, LocationType.Plant);
        var customer = await LocationAsync(s.Admin, s.DropState, "Drop Town", 21.00, 73.85, LocationType.Customer);
        var midway = await LocationAsync(s.Admin, s.OriginState, "Mid Town", 19.80, 73.85, LocationType.Customer);

        var forward = await (await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(6_000m) with { PickupLocationId = depot.Id, DropLocationId = customer.Id })).ReadAsync<OrderDto>();
        var returnOrder = await (await s.Admin.PostJsonAsync("/api/v1/orders",
            s.NewOrder(2_000m, direction: OrderDirection.Reverse) with { PickupLocationId = midway.Id, DropLocationId = depot.Id })).ReadAsync<OrderDto>();

        var created = await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [forward.Id, returnOrder.Id], new PlanOptions(AllowPtl: false)));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var run = await created.ReadAsync<RunDto>();

        var vehicle = run.Plan.Vehicles.ShouldHaveSingleItem();
        vehicle.Orders.Select(o => o.Kind).ToArray().ShouldBe(["Delivery", "ReturnPickup"]);
        vehicle.BackhaulSaving.ShouldBe(6_000m); // half of a 12,000 standalone return trip
        vehicle.EstimatedCost.ShouldBe(s.FlatRate + 6_000m);
        vehicle.Stops!.Select(x => x.Kind).ToArray().ShouldBe(["Pickup", "Drop", "ReturnPickup", "Return"]);
        run.Plan.Unplanned.ShouldBeEmpty();

        (await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var committed = await (await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/commit", null)).ReadAsync<RunDto>();
        var shipmentId = committed.Plan.Vehicles.Single().ShipmentId.ShouldNotBeNull();

        var shipment = await (await s.Admin.GetAsync($"/api/v1/shipments/{shipmentId}")).ReadAsync<ShipmentDto>();
        shipment.Orders.Select(o => o.IsReturn).ToArray().ShouldBe([false, true]);
        shipment.Summary.TotalWeightKg.ShouldBe(6_000m);
        shipment.Summary.Lane.ShouldContain("Drop Town".ToUpperInvariant());

        // Price comes from the outbound lane only; then the usual tender → accept → dispatch.
        var quotes = await (await s.Admin.GetAsync($"/api/v1/shipments/{shipmentId}/quotes")).ReadAsync<ShipmentQuotesDto>();
        quotes.Quotes.ShouldContain(q => q.ContractId == s.Contract.Summary.Id);
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipmentId}/tender", new TenderRequest(s.Contract.Summary.Id, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipmentId}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var dispatched = await (await s.Admin.PostAsync($"/api/v1/shipments/{shipmentId}/dispatch", null)).ReadAsync<ShipmentDto>();
        dispatched.Orders.ShouldAllBe(o => o.LrNumber != null);
    }

    [Fact]
    public async Task Delivery_windows_are_saved_validated_and_used_in_the_plan()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var depot = await LocationAsync(s.Admin, s.OriginState, "Origin Town", 18.50, 73.85, LocationType.Plant);
        var customer = await LocationAsync(s.Admin, s.DropState, "Drop Town", 19.60, 73.85, LocationType.Customer);

        var bad = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder() with { DeliveryWindowFrom = new TimeOnly(17, 0), DeliveryWindowTo = new TimeOnly(9, 0) });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ProblemCodeAsync()).ShouldBe("orders.window_invalid");

        var response = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(4_000m) with
        {
            PickupLocationId = depot.Id, DropLocationId = customer.Id, DeliveryWindowFrom = new TimeOnly(15, 0), DeliveryWindowTo = new TimeOnly(18, 0),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var order = await response.ReadAsync<OrderDto>();
        order.DeliveryWindowFrom.ShouldBe(new TimeOnly(15, 0));

        var run = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [order.Id], new PlanOptions(AllowPtl: false)))).ReadAsync<RunDto>();

        var drop = run.Plan.Vehicles.Single().Stops![1];
        drop.WaitMinutes.ShouldNotBeNull().ShouldBeGreaterThan(0); // reaches the consignee before 15:00 and waits
        drop.PlannedDeparture!.Value.Hour.ShouldBeGreaterThanOrEqualTo(15);
    }
}
