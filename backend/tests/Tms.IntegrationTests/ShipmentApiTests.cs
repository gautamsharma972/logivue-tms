using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ShipmentApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_load_goes_from_order_to_delivery_with_lorry_receipts_issued_at_dispatch()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var first = await s.OrderAsync(4_000m, "Drop A");
        var second = await s.OrderAsync(6_000m, "Drop B");
        first.Number.ShouldStartWith("ORD-");

        var shipment = await s.ShipmentAsync(first, second);
        shipment.Summary.Status.ShouldBe(ShipmentStatus.Draft);
        shipment.Summary.TotalWeightKg.ShouldBe(10_000m);
        shipment.Orders.Select(o => o.OrderId).ShouldBe([first.Id, second.Id]);

        var quotes = await (await s.Admin.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}/quotes")).ReadAsync<ShipmentQuotesDto>();
        var quote = quotes.Quotes.ShouldHaveSingleItem();
        quote.ContractId.ShouldBe(s.Contract.Summary.Id);
        quote.IsCheapest.ShouldBeTrue();

        var tenderResponse = await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tender", new TenderRequest(s.Contract.Summary.Id, null));
        tenderResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await tenderResponse.Content.ReadAsStringAsync());
        var tendered = await tenderResponse.ReadAsync<ShipmentDto>();
        tendered.Summary.Status.ShouldBe(ShipmentStatus.Tendered);
        tendered.Summary.TransporterId.ShouldBe(s.Transporter.Id);
        tendered.Summary.FreightEstimate.ShouldNotBeNull();

        var accepted = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{tendered.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var acceptedDto = await accepted.ReadAsync<ShipmentDto>();
        acceptedDto.Summary.Status.ShouldBe(ShipmentStatus.Accepted);
        acceptedDto.Summary.VehicleRegistration.ShouldBe(s.Vehicle.RegistrationNumber);
        acceptedDto.Summary.Utilization.ShouldNotBeNull();

        var dispatched = await s.Admin.PostAsync($"/api/v1/shipments/{tendered.Summary.Id}/dispatch", null);
        dispatched.StatusCode.ShouldBe(HttpStatusCode.OK, await dispatched.Content.ReadAsStringAsync());
        var dispatchedDto = await dispatched.ReadAsync<ShipmentDto>();
        dispatchedDto.Summary.Status.ShouldBe(ShipmentStatus.Dispatched);
        dispatchedDto.Orders.Select(o => o.LrNumber).ShouldAllBe(lr => lr != null && lr.StartsWith("LR-"));
        dispatchedDto.Orders.Select(o => o.LrNumber).Distinct().Count().ShouldBe(2);

        var delivered = await s.Admin.PostAsync($"/api/v1/shipments/{tendered.Summary.Id}/deliver", null);
        (await delivered.ReadAsync<ShipmentDto>()).Summary.Status.ShouldBe(ShipmentStatus.Delivered);

        var order = await (await s.Admin.GetAsync($"/api/v1/orders/{first.Id}")).ReadAsync<OrderDto>();
        order.Status.ShouldBe(OrderStatus.Delivered);
        order.ShipmentNumber.ShouldBe(shipment.Summary.Number);
    }

    [Fact]
    public async Task Choosing_a_dearer_contract_than_the_cheapest_needs_a_reason()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var other = await Tms.IntegrationTests.Infrastructure.ContractApiData.ActiveTransporterAsync(s.Admin);
        var type = await Tms.IntegrationTests.Infrastructure.ContractApiData.VehicleTypeAsync(s.Admin);
        var dearer = await Tms.IntegrationTests.Infrastructure.ContractApiData.ActiveContractAsync(
            s.Admin, other.Id, Tms.Modules.Contracts.Domain.ContractType.Ftl, null,
            Tms.IntegrationTests.Infrastructure.ContractApiData.Flat(
                Tms.IntegrationTests.Infrastructure.ContractApiData.State(s.OriginState),
                Tms.IntegrationTests.Infrastructure.ContractApiData.State(s.DropState), s.FlatRate + 5_000m, type.Id));

        var shipment = await s.ShipmentAsync(await s.OrderAsync());
        var quotes = await (await s.Admin.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}/quotes")).ReadAsync<ShipmentQuotesDto>();
        quotes.Quotes.Count.ShouldBe(2);
        quotes.Quotes[0].ContractId.ShouldBe(s.Contract.Summary.Id);

        var url = $"/api/v1/shipments/{shipment.Summary.Id}/tender";
        var refused = await s.Admin.PostJsonAsync(url, new TenderRequest(dearer.Summary.Id, null));
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.ProblemCodeAsync()).ShouldBe("shipments.override_reason_required");

        var allowed = await s.Admin.PostJsonAsync(url, new TenderRequest(dearer.Summary.Id, "Cheapest vendor has no capacity this week"));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await allowed.ReadAsync<ShipmentDto>()).OverrideReason.ShouldBe("Cheapest vendor has no capacity this week");
    }

    [Fact]
    public async Task A_vendor_sees_only_their_own_tendered_loads_and_never_the_price()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var draft = await s.ShipmentAsync(await s.OrderAsync());
        var tendered = await s.TenderedAsync();

        var list = await (await s.Vendor.GetAsync("/api/v1/shipments?pageSize=200")).ReadAsync<PagedResult<ShipmentSummaryDto>>();
        list.Items.Select(x => x.Id).ShouldContain(tendered.Summary.Id);
        list.Items.Select(x => x.Id).ShouldNotContain(draft.Summary.Id);
        list.Items.ShouldAllBe(x => x.TransporterId == s.Transporter.Id);
        list.Items.ShouldAllBe(x => x.FreightEstimate == null);

        var detail = await (await s.Vendor.GetAsync($"/api/v1/shipments/{tendered.Summary.Id}")).ReadAsync<ShipmentDto>();
        detail.EstimateLines.ShouldBeNull();
        detail.OverrideReason.ShouldBeNull();
        detail.ContractReference.ShouldBeNull();

        (await s.Vendor.GetAsync($"/api/v1/shipments/{draft.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Another vendor sees nothing of this load.
        using var rival = await ShipmentScenario.CreateAsync(factory);
        (await rival.Vendor.GetAsync($"/api/v1/shipments/{tendered.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await rival.Vendor.PostJsonAsync($"/api/v1/shipments/{tendered.Summary.Id}/accept", new AcceptRequest(rival.Vehicle.Id, rival.Driver.Id)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Vendors do not plan.
        (await s.Vendor.PostJsonAsync("/api/v1/orders", s.NewOrder())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.GetAsync("/api/v1/orders")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostAsync($"/api/v1/shipments/{tendered.Summary.Id}/dispatch", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accepting_is_blocked_for_a_vehicle_with_lapsed_papers_or_too_small_for_the_load()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, fleetPapers: false, fleetSize: 1);
        var shipment = await s.TenderedAsync();

        var blocked = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id));
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ProblemCodeAsync()).ShouldBe("shipments.vehicle_non_compliant");

        var options = await (await s.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}/fleet-options")).ReadAsync<FleetOptionsDto>();
        options.Vehicles.ShouldHaveSingleItem().IsOk.ShouldBeFalse();
        options.Vehicles[0].Issues.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task An_overweight_load_cannot_be_accepted_on_a_smaller_vehicle()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var heavy = await s.OrderAsync(17_000m); // the vehicle carries 16,000 kg
        var shipment = await s.TenderedAsync(heavy);

        var response = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id));
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("shipments.overload");
    }

    [Fact]
    public async Task A_declined_load_returns_to_draft_with_the_reason_and_can_be_tendered_again()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var shipment = await s.TenderedAsync();

        var empty = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/reject", new ReasonRequest(" "));
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var rejected = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/reject", new ReasonRequest("No truck free"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());

        var draft = await (await s.Admin.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).ReadAsync<ShipmentDto>();
        draft.Summary.Status.ShouldBe(ShipmentStatus.Draft);
        draft.RejectionCount.ShouldBe(1);
        draft.LastRejectionReason.ShouldBe("No truck free");
        draft.Summary.TransporterId.ShouldBeNull();

        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tender", new TenderRequest(s.Contract.Summary.Id, null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancelling_a_shipment_releases_its_orders_for_planning_again()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync();
        var shipment = await s.ShipmentAsync(order);

        var inShipment = await (await s.Admin.GetAsync($"/api/v1/orders/{order.Id}")).ReadAsync<OrderDto>();
        inShipment.Status.ShouldBe(OrderStatus.Planned);
        (await s.Admin.PutJsonAsync($"/api/v1/orders/{order.Id}", s.NewOrder(1_000m))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/cancel", new ReasonRequest("Customer postponed"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await s.Admin.GetAsync($"/api/v1/orders/{order.Id}")).ReadAsync<OrderDto>()).Status.ShouldBe(OrderStatus.Open);
    }

    [Fact]
    public async Task Orders_can_only_be_put_on_a_shipment_from_the_same_pickup_and_only_once()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync();
        var elsewhere = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder() with { Pickup = ShipmentScenario.Party(s.OriginState, "Another City") });
        var other = await elsewhere.ReadAsync<OrderDto>();

        var mixed = await s.Admin.PostJsonAsync("/api/v1/shipments", new CreateShipmentRequest([order.Id, other.Id], FreightMode.Ftl, s.VehicleTypeId, ContractApiData.Today, null));
        mixed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await mixed.ProblemCodeAsync()).ShouldBe("shipments.pickup_mismatch");

        await s.ShipmentAsync(order);
        var again = await s.Admin.PostJsonAsync("/api/v1/shipments", new CreateShipmentRequest([order.Id], FreightMode.Ftl, s.VehicleTypeId, ContractApiData.Today, null));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Planning_suggests_a_load_and_advises_between_full_and_part_truck()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(6_000m);
        var b = await s.OrderAsync(7_000m);

        var suggestions = await (await s.Admin.GetAsync("/api/v1/planning/suggestions")).ReadAsync<List<SuggestedLoadDto>>();
        var mine = suggestions.Single(l => l.OrderIds.Contains(a.Id));
        mine.OrderIds.ShouldContain(b.Id);
        mine.TotalWeightKg.ShouldBe(13_000m);
        mine.Vehicle.ShouldNotBeNull();

        var advice = await s.Admin.PostJsonAsync("/api/v1/planning/advice", new AdviceRequest(13_000m, null, s.OriginState, null, s.DropState, null, null, null));
        advice.StatusCode.ShouldBe(HttpStatusCode.OK, await advice.Content.ReadAsStringAsync());
        var dto = await advice.ReadAsync<AdviceDto>();
        dto.Recommended.ShouldNotBeNull();
        dto.Modes.Single(m => m.Mode == FreightMode.Ftl).Feasible.ShouldBeTrue();
        dto.Modes.Single(m => m.Mode == FreightMode.Ptl).Feasible.ShouldBeFalse();
        dto.RecommendedMode.ShouldBe(FreightMode.Ftl);
    }

    [Fact]
    public async Task Utilization_reports_how_full_dispatched_trucks_were()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync(); // 5,000 kg on a 16,000 kg vehicle
        (await s.Admin.PostAsync($"/api/v1/shipments/{accepted.Summary.Id}/dispatch", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await (await s.Admin.GetAsync($"/api/v1/planning/utilization?transporterId={s.Transporter.Id}")).ReadAsync<UtilizationDto>();
        var row = report.Rows.ShouldHaveSingleItem();
        row.Utilization.ShouldNotBeNull().ShouldBe(0.3125m, 0.001m);
        report.UnderUtilised.ShouldBe(1);
    }

    [Fact]
    public async Task Orders_and_shipments_belong_to_one_tenant()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync();
        var shipment = await s.ShipmentAsync(order);
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);

        (await acme.GetAsync($"/api/v1/orders/{order.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var list = await (await acme.GetAsync("/api/v1/orders?pageSize=200")).ReadAsync<PagedResult<OrderDto>>();
        list.Items.ShouldNotContain(o => o.Id == order.Id);
    }
}
