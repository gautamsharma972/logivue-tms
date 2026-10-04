using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Application.Operations;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Paging;
using OpsReason = Tms.Modules.Transporters.Application.Operations.ReasonRequest;
using ShipmentReason = Tms.Modules.Shipments.Application.ReasonRequest;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterOperationsApiTests(TmsApiFactory factory)
{
    private static DateOnly Today => ContractApiData.Today;

    private static string Range() => $"from={Today.AddDays(-60):yyyy-MM-dd}&to={Today.AddDays(30):yyyy-MM-dd}";

    private static async Task<PlacementDto> PlacementOfAsync(ShipmentScenario s, Guid shipmentId) =>
        (await (await s.Admin.GetAsync($"/api/v1/placements?transporterId={s.Transporter.Id}&pageSize=100")).ReadAsync<PagedResult<PlacementDto>>()).Items.Single(p => p.ShipmentId == shipmentId);

    private static async Task<ExecutionDto> ExecutionOfAsync(ShipmentScenario s, Guid shipmentId) =>
        (await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/executions")).ReadAsync<List<ExecutionDto>>()).Single(e => e.ShipmentId == shipmentId);

    /// <summary>A shipment planned for a day in the past, accepted but not yet moved: its pickup and placement are overdue.</summary>
    private static async Task<ShipmentDto> AcceptedInThePastAsync(ShipmentScenario s, int daysAgo = 3)
    {
        var order = await s.OrderAsync();
        var created = await s.Admin.PostJsonAsync("/api/v1/shipments", new CreateShipmentRequest([order.Id], Tms.SharedKernel.Contracts.FreightMode.Ftl, s.VehicleTypeId, Today.AddDays(-daysAgo), null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var shipment = await created.ReadAsync<ShipmentDto>();
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tender", new TenderRequest(s.Contract.Summary.Id, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var accepted = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        return await accepted.ReadAsync<ShipmentDto>();
    }

    [Fact]
    public async Task Accepting_a_load_creates_its_placement_with_the_vehicle_named_and_the_vendor_reports_it_then_staff_place_it()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();

        var placement = await PlacementOfAsync(s, accepted.Summary.Id);
        placement.Status.ShouldBe(PlacementStatus.VehicleAssigned);
        placement.VehicleRegistration.ShouldBe(s.Vehicle.RegistrationNumber);
        placement.RequiredAt.ShouldBeLessThan(DateTimeOffset.UtcNow.AddDays(2)); // before the planned pickup

        var reported = await s.Vendor.PostAsync($"/api/v1/placements/{placement.Id}/report", null);
        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync());
        (await reported.ReadAsync<PlacementDto>()).Status.ShouldBe(PlacementStatus.Reported);

        (await s.Vendor.PostAsync($"/api/v1/placements/{placement.Id}/place", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // a vendor says the vehicle is coming, staff say it arrived
        var placed = await s.Admin.PostAsync($"/api/v1/placements/{placement.Id}/place", null);
        placed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await placed.ReadAsync<PlacementDto>()).Status.ShouldBe(PlacementStatus.Placed);
        (await s.Admin.PostAsync($"/api/v1/placements/{placement.Id}/place", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Arriving_and_starting_to_load_move_the_placement_on_by_themselves()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();
        var execution = await ExecutionOfAsync(s, accepted.Summary.Id);
        var now = DateTimeOffset.UtcNow;

        (await s.Vendor.PostJsonAsync($"/api/v1/executions/{execution.Id}/events", new RecordExecutionEventRequest(ExecutionEventType.VehicleArrival, now.AddMinutes(-20)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlacementOfAsync(s, accepted.Summary.Id)).Status.ShouldBe(PlacementStatus.Placed);

        (await s.Vendor.PostJsonAsync($"/api/v1/executions/{execution.Id}/events", new RecordExecutionEventRequest(ExecutionEventType.LoadingStart, now.AddMinutes(-5)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlacementOfAsync(s, accepted.Summary.Id)).Status.ShouldBe(PlacementStatus.LoadingStarted);
    }

    [Fact]
    public async Task A_no_show_is_refused_until_the_vehicle_is_really_late_and_then_counts_against_the_transporter()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var early = await AcceptedInThePastAsync(s, daysAgo: -2); // planned for the day after tomorrow, so not yet due whatever time of day the test runs
        var future = await PlacementOfAsync(s, early.Summary.Id);
        var tooEarly = await s.Admin.PostJsonAsync($"/api/v1/placements/{future.Id}/no-show", new OpsReason("Did not come"));
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await tooEarly.ProblemCodeAsync()).ShouldBe("placements.no_show_too_early");

        var late = await AcceptedInThePastAsync(s);
        var placement = await PlacementOfAsync(s, late.Summary.Id);
        (await s.Admin.PostJsonAsync($"/api/v1/placements/{placement.Id}/no-show", new OpsReason(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var noShow = await s.Admin.PostJsonAsync($"/api/v1/placements/{placement.Id}/no-show", new OpsReason("Did not come"));
        noShow.StatusCode.ShouldBe(HttpStatusCode.OK, await noShow.Content.ReadAsStringAsync());

        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{Range()}")).ReadAsync<PerformanceDto>();
        performance.Kpis.Single(k => k.Kpi == KpiType.NoShowRate).Numerator.ShouldBe(1);
        performance.Kpis.Single(k => k.Kpi == KpiType.PlacementCompliance).Value.ShouldBe(0m);
        var alerts = await (await s.Admin.GetAsync("/api/v1/transporter-alerts?pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        alerts.Items.ShouldContain(a => a.AlertType == "PLACEMENT_NO_SHOW" && a.ShipmentId == late.Summary.Id);
    }

    [Fact]
    public async Task Swapping_the_vehicle_before_it_leaves_counts_as_a_replacement()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();
        var vehicles = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/vehicles?pageSize=50")).ReadAsync<PagedResult<VehicleDto>>();
        var drivers = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/drivers?pageSize=50")).ReadAsync<PagedResult<DriverDto>>();
        var other = vehicles.Items.First(v => v.Id != s.Vehicle.Id);

        var swapped = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{accepted.Summary.Id}/reassign", new AcceptRequest(other.Id, drivers.Items[0].Id));
        swapped.StatusCode.ShouldBe(HttpStatusCode.OK, await swapped.Content.ReadAsStringAsync());

        var placement = await PlacementOfAsync(s, accepted.Summary.Id);
        placement.ReplacementCount.ShouldBe(1);
        placement.VehicleRegistration.ShouldBe(other.RegistrationNumber);
        placement.Events.Select(e => e.EventType).ShouldContain("VehicleReplaced");
    }

    [Fact]
    public async Task Cancelling_a_shipment_closes_its_placement_and_load_and_stops_the_chasing()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await AcceptedInThePastAsync(s);
        var before = await (await s.Admin.GetAsync("/api/v1/transporter-alerts?pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        before.Items.ShouldContain(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "PICKUP_OVERDUE" && a.Status != AlertStatus.Resolved);
        before.Items.ShouldContain(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "PLACEMENT_OVERDUE" && a.Status != AlertStatus.Resolved);

        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{accepted.Summary.Id}/cancel", new ShipmentReason("Customer cancelled"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PlacementOfAsync(s, accepted.Summary.Id)).Status.ShouldBe(PlacementStatus.Cancelled);
        (await ExecutionOfAsync(s, accepted.Summary.Id)).Status.ShouldBe(ExecutionStatus.Cancelled);
        var after = await (await s.Admin.GetAsync("/api/v1/transporter-alerts?pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        after.Items.Where(a => a.ShipmentId == accepted.Summary.Id).ShouldAllBe(a => a.Status == AlertStatus.Resolved);
    }

    [Fact]
    public async Task A_load_that_is_overdue_raises_an_alert_that_clears_itself_when_the_load_moves()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await AcceptedInThePastAsync(s);

        var open = await (await s.Admin.GetAsync($"/api/v1/transporter-alerts?transporterId={s.Transporter.Id}&pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        var pickup = open.Items.Single(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "PICKUP_OVERDUE");
        pickup.TransporterName.ShouldBe(s.Transporter.LegalName);
        pickup.Message.ShouldContain("overdue");

        var acknowledged = await s.Admin.PostAsync($"/api/v1/transporter-alerts/{pickup.Id}/acknowledge", null);
        acknowledged.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await acknowledged.ReadAsync<AlertDto>()).Status.ShouldBe(AlertStatus.Acknowledged);
        (await s.Admin.PostAsync($"/api/v1/transporter-alerts/{pickup.Id}/acknowledge", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await s.Admin.PostAsync($"/api/v1/shipments/{accepted.Summary.Id}/dispatch", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await (await s.Admin.GetAsync($"/api/v1/transporter-alerts?transporterId={s.Transporter.Id}&pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        after.Items.Single(a => a.Id == pickup.Id).Status.ShouldBe(AlertStatus.Resolved);

        // The same problem is raised once, however often the list is read.
        (await (await s.Admin.GetAsync($"/api/v1/transporter-alerts?transporterId={s.Transporter.Id}&pageSize=100")).ReadAsync<PagedResult<AlertDto>>())
            .Items.Count(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "PLACEMENT_OVERDUE").ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task A_late_pickup_with_no_reason_asks_for_one_and_the_alert_goes_when_a_reason_is_given()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await AcceptedInThePastAsync(s);
        (await s.Admin.PostAsync($"/api/v1/shipments/{accepted.Summary.Id}/dispatch", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var execution = await ExecutionOfAsync(s, accepted.Summary.Id);
        execution.PickupAttribution.ShouldBe(DelayAttribution.Unattributed); // days late, nobody has said why

        var alerts = await (await s.Admin.GetAsync($"/api/v1/transporter-alerts?transporterId={s.Transporter.Id}&pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        alerts.Items.ShouldContain(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "DELAY_ATTRIBUTION_REQUIRED" && a.Status != AlertStatus.Resolved);

        (await s.Admin.PostJsonAsync($"/api/v1/executions/{execution.Id}/delay", new AttributeDelayRequest(false, "VEHICLE_BREAKDOWN"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await (await s.Admin.GetAsync($"/api/v1/transporter-alerts?transporterId={s.Transporter.Id}&pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        after.Items.Single(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "DELAY_ATTRIBUTION_REQUIRED").Status.ShouldBe(AlertStatus.Resolved);
        after.Items.ShouldContain(a => a.ShipmentId == accepted.Summary.Id && a.AlertType == "CARRIER_PICKUP_DELAY" && a.Status != AlertStatus.Resolved);

        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{Range()}")).ReadAsync<PerformanceDto>();
        performance.Kpis.Single(k => k.Kpi == KpiType.OnTimePickup).Denominator.ShouldBeGreaterThanOrEqualTo(1); // now it counts against the carrier
    }

    [Fact]
    public async Task Goods_that_arrive_damaged_start_a_claim_whose_value_is_filled_in_then_resolved()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();
        (await s.Admin.PostAsync($"/api/v1/shipments/{accepted.Summary.Id}/dispatch", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var orderId = accepted.Orders[0].OrderId;
        var delivered = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{accepted.Summary.Id}/orders/{orderId}/delivery",
            new Tms.Modules.Shipments.Application.Delivery.RecordDeliveryRequest(null, "Receiver", 40, 3, "Crates broken"));
        delivered.StatusCode.ShouldBe(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());

        var claims = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/claims?{Range()}")).ReadAsync<List<ClaimDto>>();
        var claim = claims.Single(c => c.ShipmentId == accepted.Summary.Id);
        claim.ClaimType.ShouldBe(ClaimType.Damage);
        claim.ClaimValue.ShouldBe(0m);
        claim.Remarks!.ShouldContain("damaged");

        (await s.Admin.PutJsonAsync($"/api/v1/claims/{claim.Id}/value", new SetClaimValueRequest(-5m))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PutJsonAsync($"/api/v1/claims/{claim.Id}/value", new SetClaimValueRequest(12_500m))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Admin.PostAsync($"/api/v1/claims/{claim.Id}/resolve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Admin.PostAsync($"/api/v1/claims/{claim.Id}/resolve", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{Range()}")).ReadAsync<PerformanceDto>();
        performance.Kpis.Single(k => k.Kpi == KpiType.ClaimsRate).Numerator.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task A_claim_can_be_recorded_by_hand_only_against_the_transporters_own_shipment()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var other = await ShipmentScenario.CreateAsync(factory);
        var theirs = await other.AcceptedAsync();

        var wrong = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/claims", new RecordClaimRequest(ClaimType.LossTheft, Today, 5_000m, theirs.Summary.Id, "x"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/claims", new RecordClaimRequest(ClaimType.LossTheft, Today.AddDays(2), 5_000m, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var ok = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/claims", new RecordClaimRequest(ClaimType.LossTheft, Today, 5_000m, null, "Pilferage"));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        (await s.Vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/claims", new RecordClaimRequest(ClaimType.Damage, Today, 1m, null, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/claims?{Range()}")).StatusCode.ShouldBe(HttpStatusCode.OK); // a vendor may see claims against itself
    }

    [Fact]
    public async Task Cost_is_recorded_once_per_load_against_the_agreed_estimate_and_is_staff_only()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();

        var onBudget = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/costs", new RecordLoadCostRequest(accepted.Summary.Id, s.FlatRate));
        onBudget.StatusCode.ShouldBe(HttpStatusCode.Created, await onBudget.Content.ReadAsStringAsync());
        var cost = await onBudget.ReadAsync<LoadCostDto>();
        cost.AgreedAmount.ShouldBe(s.FlatRate); // defaults to the estimate the load was accepted at
        cost.OnBudget.ShouldBeTrue();
        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/costs", new RecordLoadCostRequest(accepted.Summary.Id, s.FlatRate))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{Range()}")).ReadAsync<PerformanceDto>();
        performance.Kpis.Single(k => k.Kpi == KpiType.CostPerformance).Numerator.ShouldBeGreaterThanOrEqualTo(1);

        (await s.Vendor.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/costs?{Range()}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // prices are not for vendors
        (await s.Vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/costs", new RecordLoadCostRequest(accepted.Summary.Id, 1m))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_vendor_reports_its_daily_capacity_and_availability_is_measured_from_it()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var other = await ShipmentScenario.CreateAsync(factory);

        var saved = await s.Vendor.PutJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capacity", new SaveCapacityRequest(Today, 10, 8));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await s.Vendor.PutJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capacity", new SaveCapacityRequest(Today, 10, 9))).StatusCode.ShouldBe(HttpStatusCode.OK); // saving a day replaces it
        (await s.Vendor.PutJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capacity", new SaveCapacityRequest(Today, 5, 6))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await other.Vendor.PutJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capacity", new SaveCapacityRequest(Today, 1, 1))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var days = await (await s.Vendor.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/capacity?{Range()}")).ReadAsync<List<CapacityDayDto>>();
        days.Single().VehiclesAvailable.ShouldBe(9);
        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{Range()}")).ReadAsync<PerformanceDto>();
        var availability = performance.Kpis.Single(k => k.Kpi == KpiType.Availability);
        (availability.Numerator, availability.Denominator, availability.Value).ShouldBe((9m, 10m, 90m));
    }

    [Fact]
    public async Task Placements_are_scoped_to_the_vendors_own_company_and_alerts_are_staff_only()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var other = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();
        var placement = await PlacementOfAsync(s, accepted.Summary.Id);

        (await other.Vendor.GetAsync($"/api/v1/placements/{placement.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Vendor.PostAsync($"/api/v1/placements/{placement.Id}/report", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var mine = await (await s.Vendor.GetAsync($"/api/v1/placements?transporterId={other.Transporter.Id}")).ReadAsync<PagedResult<PlacementDto>>();
        mine.Items.ShouldAllBe(p => p.TransporterId == s.Transporter.Id); // asking for another company's list returns only its own
        (await s.Vendor.GetAsync("/api/v1/transporter-alerts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostAsync("/api/v1/transporter-alerts/evaluate", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
