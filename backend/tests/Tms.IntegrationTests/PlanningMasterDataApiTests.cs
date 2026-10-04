using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Transporters.Application;
using Tms.SharedKernel.Contracts;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlanningMasterDataApiTests(TmsApiFactory factory)
{
    private static async Task<RunDto> PlanAsync(ShipmentScenario s, PlanOptions? options, params OrderDto[] orders)
    {
        var response = await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, orders.Select(o => o.Id).ToList(), options));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RunDto>();
    }

    private static async Task SetAvailabilityAsync(ShipmentScenario s, FleetAvailability availability, DateOnly? from)
    {
        var current = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/vehicles")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<VehicleDto>>();
        var vehicle = current.Items.Single(v => v.Id == s.Vehicle.Id);
        var response = await s.Admin.PutJsonAsync($"/api/v1/vehicles/{vehicle.Id}",
            new SaveVehicleRequest(vehicle.RegistrationNumber, vehicle.VehicleTypeId, vehicle.Ownership, vehicle.Make, vehicle.YearOfManufacture, true, vehicle.Version, availability, from, null, "Workshop"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_trip_is_given_a_real_vehicle_and_driver_and_two_trips_never_share_them()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(4_000m, "Drop A");
        var b = await s.OrderAsync(5_000m, "Drop B");

        var run = await PlanAsync(s, new PlanOptions(AllowPtl: false, AllowConsolidation: false), a, b);

        run.Plan.Vehicles.Count.ShouldBe(2);
        run.Plan.Vehicles.ShouldAllBe(v => v.AssignedVehicle != null && v.AssignedDriver != null && v.Transporter != null);
        run.Plan.Vehicles.Select(v => v.AssignedVehicle!.Id).Distinct().Count().ShouldBe(2);
        run.Plan.Vehicles.Select(v => v.AssignedDriver!.Id).Distinct().Count().ShouldBe(2);
        run.Plan.Vehicles[0].Transporter!.Id.ShouldBe(s.Transporter.Id);
        run.Plan.Vehicles[0].Alternatives.Single(x => x.Chosen).Vehicle.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_vehicle_in_the_workshop_is_not_planned_and_the_order_says_why_then_it_is_once_the_vehicle_is_back()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, fleetSize: 1);
        var order = await s.OrderAsync(4_000m);
        await SetAvailabilityAsync(s, FleetAvailability.InMaintenance, ContractApiData.Today.AddDays(3));

        var blocked = await PlanAsync(s, null, order);

        blocked.Plan.Vehicles.ShouldBeEmpty();
        var unplanned = blocked.Plan.Unplanned.ShouldHaveSingleItem();
        unplanned.Code.ShouldBe(UnplannedCodes.NoAvailableVehicle);
        unplanned.Reason.ShouldContain("in maintenance");

        await SetAvailabilityAsync(s, FleetAvailability.Available, null);
        var planned = await PlanAsync(s, null, order);

        planned.Plan.Vehicles.ShouldHaveSingleItem().AssignedVehicle!.Registration.ShouldBe(s.Vehicle.RegistrationNumber);
    }

    [Fact]
    public async Task Planning_can_be_told_not_to_insist_on_a_free_vehicle()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, fleetSize: 1);
        var order = await s.OrderAsync(4_000m);
        await SetAvailabilityAsync(s, FleetAvailability.OffRoad, null);

        var run = await PlanAsync(s, new PlanOptions(RequireAvailableVehicle: false), order);

        run.Plan.Vehicles.ShouldHaveSingleItem().AssignedVehicle.ShouldBeNull();
    }

    [Fact]
    public async Task Incompatible_categories_never_share_a_vehicle_and_the_rule_can_be_managed()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var food = await (await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(2_000m, "Same Drop") with { ProductCategory = "food" })).ReadAsync<OrderDto>();
        var chemicals = await (await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(2_000m, "Same Drop") with { ProductCategory = "chemicals" })).ReadAsync<OrderDto>();
        food.ProductCategory.ShouldBe("FOOD");

        (await PlanAsync(s, new PlanOptions(AllowPtl: false), food, chemicals)).Plan.Vehicles.ShouldHaveSingleItem(); // nothing forbids sharing yet

        var created = await s.Admin.PostJsonAsync("/api/v1/planning/compatibility-rules", new SaveCompatibilityRuleRequest("Chemicals", "FOOD", "Contamination"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var rule = await created.ReadAsync<CompatibilityRuleDto>();
        rule.CategoryA.ShouldBe("CHEMICALS");
        (await s.Admin.PostJsonAsync("/api/v1/planning/compatibility-rules", new SaveCompatibilityRuleRequest("food", "chemicals", null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var split = await PlanAsync(s, new PlanOptions(AllowPtl: false), food, chemicals);
        split.Plan.Vehicles.Count.ShouldBe(2);
        split.Plan.Unplanned.ShouldBeEmpty();

        (await s.Admin.DeleteAsync($"/api/v1/planning/compatibility-rules/{rule.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await s.Admin.GetAsync("/api/v1/planning/compatibility-rules")).ReadAsync<List<CompatibilityRuleDto>>()).ShouldNotContain(r => r.Id == rule.Id);
    }

    [Fact]
    public async Task A_return_order_needs_a_type_and_a_reason_and_a_forward_order_cannot_have_them()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);

        var noReason = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(1_000m, direction: OrderDirection.Reverse) with { ReturnType = null, ReturnReason = null });
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noReason.Content.ReadAsStringAsync()).ShouldContain("returnType");

        var forwardWithReturn = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(1_000m) with { ReturnType = Tms.Modules.Shipments.Domain.ReturnType.CustomerReturn, ReturnReason = "x" });
        forwardWithReturn.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder(1_000m, direction: OrderDirection.Reverse) with { PickupWindowFrom = new TimeOnly(9, 0), PickupWindowTo = new TimeOnly(12, 0) });
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var dto = await ok.ReadAsync<OrderDto>();
        dto.ReturnType.ShouldBe(Tms.Modules.Shipments.Domain.ReturnType.CustomerReturn);
        dto.PickupWindowFrom.ShouldBe(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task A_run_can_be_read_as_vehicles_and_stops_and_carries_cost_per_tonne_and_per_shipment()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(4_000m, "Drop A");
        var b = await s.OrderAsync(5_000m, "Drop B");
        var run = await PlanAsync(s, new PlanOptions(AllowPtl: false, AllowConsolidation: false), a, b);

        var vehicles = await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/vehicles")).ReadAsync<List<RunVehicleDto>>();
        var stops = await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/stops");

        vehicles.Count.ShouldBe(2);
        vehicles.ShouldAllBe(v => v.Vehicle != null && v.Driver != null && v.TransporterName != null);
        stops.StatusCode.ShouldBe(HttpStatusCode.OK);
        run.Plan.Summary.CostPerShipment.ShouldBe(s.FlatRate);
        run.Plan.Summary.CostPerTonne.ShouldBe(Math.Round(s.FlatRate * 2 / 9m, 2));
    }

    [Fact]
    public async Task Consolidation_can_be_previewed_without_saving_anything()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(2_000m, "Same Drop");
        var b = await s.OrderAsync(3_000m, "Same Drop");

        var response = await s.Admin.PostJsonAsync("/api/v1/planning/consolidation/preview", new CreateRunRequest(ContractApiData.Today, [a.Id, b.Id], new PlanOptions(AllowPtl: false)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var preview = await response.ReadAsync<ConsolidationPreviewDto>();
        preview.Groups.ShouldHaveSingleItem().OrderNumbers.Count.ShouldBe(2);
        preview.OrdersConsolidated.ShouldBe(2);
        preview.TotalSaving.ShouldBe(s.FlatRate); // two trucks of 40,000 become one
        var orders = await (await s.Admin.GetAsync("/api/v1/planning/orders/unplanned")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<OrderDto>>();
        orders.Items.Select(o => o.Id).ShouldContain(a.Id); // nothing was planned or saved
    }

    [Fact]
    public async Task Planning_returns_needs_at_least_one_return_order()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var forward = await s.OrderAsync(2_000m);

        var response = await s.Admin.PostJsonAsync("/api/v1/planning/returns/plan", new CreateRunRequest(ContractApiData.Today, [forward.Id], null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe("planning.no_returns");
    }

    [Fact]
    public async Task A_background_run_is_returned_as_running_then_finishes_with_its_log()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(4_000m, "Drop A");
        var b = await s.OrderAsync(5_000m, "Drop B");

        var created = await s.Admin.PostJsonAsync("/api/v1/planning/runs",
            new CreateRunRequest(ContractApiData.Today, [a.Id, b.Id], new PlanOptions(AllowPtl: false, AllowConsolidation: false), Background: true));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var run = await created.ReadAsync<RunDto>();
        run.Status.ShouldBe(PlanStatus.Running);
        run.Plan.Vehicles.ShouldBeEmpty();

        RunDto finished = run;
        for (var attempt = 0; attempt < 100 && finished.Status == PlanStatus.Running; attempt++)
        {
            await Task.Delay(200);
            finished = await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}")).ReadAsync<RunDto>();
        }

        finished.Status.ShouldBe(PlanStatus.Completed);
        finished.Plan.Vehicles.Count.ShouldBe(2);
        finished.StartedAt.ShouldNotBeNull();
        finished.CompletedAt.ShouldNotBeNull();
        finished.Log.ShouldNotBeNull().Select(l => l.Message).ShouldContain(m => m.StartsWith("Finished in", StringComparison.Ordinal));
        (await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/approve", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_mode_decision_is_kept_with_the_plan_and_an_override_needs_a_reason()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync(4_000m);

        var noReason = await s.Admin.PostJsonAsync("/api/v1/planning/runs",
            new CreateRunRequest(ContractApiData.Today, [order.Id], null, ModeChoice: new ModeChoice(FreightMode.Ptl, Override: true)));
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var accepted = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs",
            new CreateRunRequest(ContractApiData.Today, [order.Id], null, ModeChoice: new ModeChoice(FreightMode.Ftl, Override: false)))).ReadAsync<RunDto>();
        accepted.Reason.ShouldBe("Recommendation accepted: full truck.");
        accepted.Options.AllowPtl.ShouldBeFalse();
        accepted.Plan.Vehicles.ShouldHaveSingleItem().Mode.ShouldBe(FreightMode.Ftl);

        var overridden = await s.Admin.PostJsonAsync("/api/v1/planning/runs",
            new CreateRunRequest(ContractApiData.Today, [order.Id], null, ModeChoice: new ModeChoice(FreightMode.Ptl, Override: true, "Customer wants it shared")));
        overridden.StatusCode.ShouldBe(HttpStatusCode.Created, await overridden.Content.ReadAsStringAsync());
        var run = await overridden.ReadAsync<RunDto>();
        run.Reason.ShouldNotBeNull().ShouldContain("overridden");
        run.Reason.ShouldContain("Customer wants it shared");
        run.Options.AllowFtl.ShouldBeFalse();
    }
}
