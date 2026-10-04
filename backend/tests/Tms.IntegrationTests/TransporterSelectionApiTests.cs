using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Application.Selection;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterSelectionApiTests(TmsApiFactory factory)
{
    private static DateOnly Today => ContractApiData.Today;

    private static SelectionRequest Request(ShipmentScenario s, IReadOnlyList<string>? capabilities = null, bool urgent = false) =>
        new(s.OriginState, "Origin City", s.DropState, "Drop City", FreightMode.Ftl, s.VehicleTypeId, 5_000m, 20m, Today, capabilities, urgent);

    private static async Task<LaneDto> AddLaneAsync(ShipmentScenario s)
    {
        var response = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/lanes",
            new SaveLaneRequest(s.OriginState, null, s.DropState, null, null, 720, Today.AddDays(-30), null, true, null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<LaneDto>();
    }

    private static async Task<CandidateEvaluation> EvaluateAsync(ShipmentScenario s, SelectionRequest request)
    {
        var response = await s.Admin.PostJsonAsync("/api/v1/transporters/eligibility", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<List<CandidateEvaluation>>()).Single(c => c.TransporterId == s.Transporter.Id);
    }

    [Fact]
    public async Task A_transporter_becomes_eligible_once_it_has_a_lane_and_is_explained_while_it_has_not()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);

        var before = await EvaluateAsync(s, Request(s));
        before.Eligible.ShouldBeFalse();
        before.Reasons.ShouldContain(r => r.StartsWith("Lane not configured"));
        before.Rate.ShouldNotBeNull(); // the contract prices it; only the lane is missing
        before.Rate!.Total.ShouldBe(s.FlatRate);

        await AddLaneAsync(s);
        var after = await EvaluateAsync(s, Request(s));
        after.Eligible.ShouldBeTrue(string.Join("; ", after.Reasons));
        after.AvailableVehicles.ShouldBe(3);
        after.LaneId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_required_capability_must_be_held_and_ending_it_removes_eligibility()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await AddLaneAsync(s);
        var needs = Request(s, [CapabilityCatalog.Hazardous]);
        (await EvaluateAsync(s, needs)).Reasons.ShouldContain(r => r.Contains("HAZARDOUS"));

        var added = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capabilities", new AddCapabilityRequest("hazardous", Today.AddDays(-1), null));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        var capability = await added.ReadAsync<CapabilityDto>();
        capability.Code.ShouldBe("HAZARDOUS");
        (await EvaluateAsync(s, needs)).Eligible.ShouldBeTrue();
        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capabilities", new AddCapabilityRequest("HAZARDOUS", Today, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/capabilities", new AddCapabilityRequest("TELEPORTATION", Today, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await s.Admin.PostAsync($"/api/v1/capabilities/{capability.Id}/end", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EvaluateAsync(s, needs)).Eligible.ShouldBeFalse();
        (await s.Admin.PostAsync($"/api/v1/capabilities/{capability.Id}/end", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_planning_rule_bars_the_transporter_everywhere_with_its_reason_until_it_is_ended()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await AddLaneAsync(s);

        var blank = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/planning-rules", new AddPlanningRuleRequest(PlanningRuleType.DoNotAllocate, null, " ", Today, null));
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var created = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/planning-rules", new AddPlanningRuleRequest(PlanningRuleType.DoNotAllocate, null, "Repeated no-shows in August", Today.AddDays(-1), null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var rule = await created.ReadAsync<PlanningRuleDto>();

        var barred = await EvaluateAsync(s, Request(s));
        barred.Eligible.ShouldBeFalse();
        barred.Reasons.ShouldContain(r => r.Contains("Repeated no-shows in August"));
        barred.RestrictedForPlanning.ShouldBeTrue();

        (await s.Admin.PostJsonAsync($"/api/v1/planning-rules/{rule.Id}/end", new EndPlanningRuleRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync($"/api/v1/planning-rules/{rule.Id}/end", new EndPlanningRuleRequest("Reviewed with the transporter"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EvaluateAsync(s, Request(s))).Eligible.ShouldBeTrue();
        (await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/planning-rules")).ReadAsync<List<PlanningRuleDto>>()).Single().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task The_recommendation_ranks_eligible_carriers_and_explains_the_pick()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await AddLaneAsync(s);

        var response = await s.Admin.PostJsonAsync("/api/v1/transporters/recommendation", Request(s));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<RecommendationResult>();
        var mine = result.Ranked.Single(r => r.Candidate.TransporterId == s.Transporter.Id);
        mine.Components.Select(c => c.Factor).ShouldContain("Rate");
        mine.Explanations.ShouldNotBeEmpty();
        mine.Components.Where(c => !c.Sufficient).ShouldAllBe(c => c.Score == 60m); // no history yet: neutral, not zero
        result.Candidates.ShouldContain(c => c.TransporterId == s.Transporter.Id);
    }

    [Fact]
    public async Task Planning_does_not_give_a_load_to_a_barred_transporter_and_says_why()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync(4_000m);
        var created = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/planning-rules",
            new AddPlanningRuleRequest(PlanningRuleType.DoNotAllocate, null, "Under investigation", Today.AddDays(-1), null));
        var rule = await created.ReadAsync<PlanningRuleDto>();

        var barred = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(Today, [order.Id], null))).ReadAsync<RunDto>();
        barred.Plan.Vehicles.ShouldBeEmpty();
        var left = barred.Plan.Unplanned.ShouldHaveSingleItem();
        left.Code.ShouldBe(UnplannedCodes.TransporterRestricted);
        left.Reason.ShouldContain("Under investigation");

        await s.Admin.PostJsonAsync($"/api/v1/planning-rules/{rule.Id}/end", new EndPlanningRuleRequest("Cleared"));
        var planned = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(Today, [order.Id], null))).ReadAsync<RunDto>();
        planned.Plan.Vehicles.ShouldHaveSingleItem().TransporterId.ShouldBe(s.Transporter.Id);
    }

    [Fact]
    public async Task Overlapping_lanes_for_the_same_route_are_refused()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await AddLaneAsync(s);

        var overlap = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/lanes",
            new SaveLaneRequest(s.OriginState, null, s.DropState, null, null, 600, Today, null, true, null));

        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overlap.ProblemCodeAsync()).ShouldBe("lanes.overlap");
        var different = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/lanes",
            new SaveLaneRequest(s.OriginState, null, s.DropState, null, FreightMode.Ptl, 600, Today, null, true, null));
        different.StatusCode.ShouldBe(HttpStatusCode.Created); // a different service is a different lane
    }

    [Fact]
    public async Task Selection_is_for_planners_not_vendors_and_a_vendor_never_sees_the_decisions_made_about_it()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await AddLaneAsync(s);
        using var planner = await factory.UserWithPermissionsAsync(s.Admin, "transporters.select");
        using var nobody = await factory.UserWithPermissionsAsync(s.Admin, "transporters.read");

        (await planner.PostJsonAsync("/api/v1/transporters/eligibility", Request(s))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await planner.PostJsonAsync("/api/v1/transporters/recommendation", Request(s))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await planner.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.PostJsonAsync("/api/v1/transporters/eligibility", Request(s))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostJsonAsync("/api/v1/transporters/eligibility", Request(s))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/planning-rules")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/planning-rules", new AddPlanningRuleRequest(PlanningRuleType.PreferredCarrier, null, "x", Today, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var bad = await planner.PostJsonAsync("/api/v1/transporters/eligibility", Request(s) with { WeightKg = 0 });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
