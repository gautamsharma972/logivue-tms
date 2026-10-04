using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlanningRunApiTests(TmsApiFactory factory)
{
    private static CreateRunRequest Request(IEnumerable<OrderDto> orders, PlanOptions? options = null) =>
        new(ContractApiData.Today, orders.Select(o => o.Id).ToList(), options ?? new PlanOptions(AllowPtl: false));

    private static async Task<RunDto> RunAsync(ShipmentScenario s, CreateRunRequest request)
    {
        var response = await s.Admin.PostJsonAsync("/api/v1/planning/runs", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RunDto>();
    }

    [Fact]
    public async Task A_run_prices_the_orders_against_real_contracts_and_commits_into_draft_shipments()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(4_000m, "Drop A");
        var b = await s.OrderAsync(5_000m, "Drop B");

        var run = await RunAsync(s, Request([a, b]));
        run.Number.ShouldStartWith("PLN-");
        run.PlanVersion.ShouldBe(1);
        run.Status.ShouldBe(PlanStatus.Completed);
        run.Plan.SolverStatus.ShouldBe(SolverStatus.Feasible);
        var vehicle = run.Plan.Vehicles.ShouldHaveSingleItem();
        vehicle.Orders.Count.ShouldBe(2);
        vehicle.EstimatedCost.ShouldBe(s.FlatRate);
        vehicle.ContractId.ShouldBe(s.Contract.Summary.Id);
        vehicle.WeightUtilisation.ShouldNotBeNull().ShouldBe(9_000m / 16_000m, 0.001m);
        vehicle.Reason.ShouldNotBeNullOrWhiteSpace();

        (await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/commit", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // not approved yet

        var approved = await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/approve", null);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var committed = await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/commit", null);
        committed.StatusCode.ShouldBe(HttpStatusCode.OK, await committed.Content.ReadAsStringAsync());
        var done = await committed.ReadAsync<RunDto>();
        done.Status.ShouldBe(PlanStatus.Committed);
        var shipmentId = done.Plan.Vehicles.Single().ShipmentId.ShouldNotBeNull();

        var shipment = await (await s.Admin.GetAsync($"/api/v1/shipments/{shipmentId}")).ReadAsync<ShipmentDto>();
        shipment.Summary.Status.ShouldBe(ShipmentStatus.Draft);
        shipment.Orders.Count.ShouldBe(2);
        (await (await s.Admin.GetAsync($"/api/v1/orders/{a.Id}")).ReadAsync<OrderDto>()).Status.ShouldBe(OrderStatus.Planned);
    }

    [Fact]
    public async Task A_committed_plan_keeps_its_prices_when_the_contract_changes_later()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await RunAsync(s, Request([await s.OrderAsync()]));
        await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/approve", null);
        await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/commit", null);

        // A cheaper contract appears after the commit.
        var other = await ContractApiData.ActiveTransporterAsync(s.Admin);
        var type = await ContractApiData.VehicleTypeAsync(s.Admin);
        await ContractApiData.ActiveContractAsync(s.Admin, other.Id, ContractType.Ftl, null,
            ContractApiData.Flat(ContractApiData.State(s.OriginState), ContractApiData.State(s.DropState), 1_000m, type.Id));

        var again = await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}")).ReadAsync<RunDto>();
        again.Plan.Vehicles.Single().EstimatedCost.ShouldBe(s.FlatRate);
        again.Plan.Vehicles.Single().ContractId.ShouldBe(s.Contract.Summary.Id);
    }

    [Fact]
    public async Task Re_planning_adds_a_version_keeps_the_old_one_and_honours_locks()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(3_000m, "Drop A");
        var run = await RunAsync(s, Request([a]));
        var key = run.Plan.Vehicles.Single().Key;

        (await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/lock", new LockRequest(key, true))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var blank = await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/reoptimize", new ReoptimizeRequest("", null));
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var second = await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/reoptimize", new ReoptimizeRequest("Try without consolidation", new PlanOptions(AllowPtl: false, AllowConsolidation: false)));
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var v2 = await second.ReadAsync<RunDto>();
        v2.PlanVersion.ShouldBe(2);
        v2.Number.ShouldBe(run.Number);
        v2.Reason.ShouldBe("Try without consolidation");
        v2.Plan.Vehicles.ShouldContain(v => v.Key == key && v.IsLocked);

        var versions = await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/versions")).ReadAsync<List<VersionDto>>();
        versions.Select(v => v.PlanVersion).ShouldBe([2, 1]);

        // The old version is untouched, and is no longer the one to work on.
        var old = await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}")).ReadAsync<RunDto>();
        old.IsLatest.ShouldBeFalse();
        (await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/reoptimize", new ReoptimizeRequest("again", null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Orders_with_no_rate_are_reported_as_unplanned_with_suggestions()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var norate = await s.Admin.PostJsonAsync("/api/v1/orders", s.NewOrder() with { Drop = ShipmentScenario.Party("NoRate" + Guid.NewGuid().ToString("N")[..5], "Nowhere") });
        var order = await norate.ReadAsync<OrderDto>();

        var run = await RunAsync(s, Request([order]));

        run.Status.ShouldBe(PlanStatus.Infeasible);
        var unplanned = run.Plan.Unplanned.ShouldHaveSingleItem();
        unplanned.Code.ShouldBe(UnplannedCodes.NoRate);
        unplanned.Suggestions.ShouldNotBeEmpty();
        (await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_open_orders_can_be_planned_and_a_committed_order_cannot_be_planned_again()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync();
        var run = await RunAsync(s, Request([order]));
        await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/approve", null);
        await s.Admin.PostAsync($"/api/v1/planning/runs/{run.Id}/commit", null);

        var again = await s.Admin.PostJsonAsync("/api/v1/planning/runs", Request([order]));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("planning.orders_not_open");
    }

    [Fact]
    public async Task Planners_plan_but_only_approvers_approve_and_vendors_see_nothing()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await RunAsync(s, Request([await s.OrderAsync()]));
        using var planner = await factory.UserWithPermissionsAsync(s.Admin, "shipments.plan", "shipments.read");

        (await planner.PostJsonAsync("/api/v1/planning/preview", Request([await s.OrderAsync()]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await planner.PostAsync($"/api/v1/planning/runs/{run.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await s.Vendor.GetAsync($"/api/v1/planning/runs/{run.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostJsonAsync("/api/v1/planning/runs", Request([await s.OrderAsync()]))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        (await acme.GetAsync($"/api/v1/planning/runs/{run.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Options_are_validated_on_the_server()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync();

        (await s.Admin.PostJsonAsync("/api/v1/planning/runs", Request([order], new PlanOptions(AllowFtl: false, AllowPtl: false)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync("/api/v1/planning/runs", Request([order], new PlanOptions(MaxStops: 0)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [], null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Comparison_and_recommendations_explain_each_vehicle_type()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync(5_000m);

        var compare = await s.Admin.PostJsonAsync("/api/v1/planning/ftl-ptl/compare", new CompareRequest([order.Id], null, null));
        compare.StatusCode.ShouldBe(HttpStatusCode.OK, await compare.Content.ReadAsStringAsync());
        var dto = await compare.ReadAsync<ComparisonDto>();
        dto.RecommendedMode.ShouldBe(Tms.SharedKernel.Contracts.FreightMode.Ftl);
        dto.Alternatives.ShouldContain(a => a.Chosen && a.Total == s.FlatRate);

        var rec = await (await s.Admin.GetAsync("/api/v1/planning/vehicles/recommendations?weightKg=9000&volumeCbm=30")).ReadAsync<List<VehicleTypeEvaluation>>();
        rec.ShouldContain(r => r.Accepted);
        rec.Where(r => !r.Accepted).ShouldAllBe(r => r.Reason != null);

        var list = await (await s.Admin.GetAsync("/api/v1/planning/runs")).ReadAsync<PagedResult<RunSummaryDto>>();
        list.Items.ShouldNotBeEmpty();
    }
}
