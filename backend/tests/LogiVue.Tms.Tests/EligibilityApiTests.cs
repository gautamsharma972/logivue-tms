using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Api.Controllers;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Planning;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using Microsoft.EntityFrameworkCore;
using LogiVue.Tms.TransporterManagement.Application.Recommendation;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Milestone 4: eligibility, explainable recommendation, the planning contract and planning rules.</summary>
public class EligibilityApiTests : IClassFixture<TestHost>
{
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";
    private static readonly DateOnly RequestDate = new(2026, 10, 10);

    private readonly TestHost _host;
    private readonly HttpClient _admin;

    public EligibilityApiTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Client(AllRoles);
    }

    private HttpClient Client(string roles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, "tester");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private static TransporterSelectionRequest Request(FixtureLane lane, decimal weight = 12000m, string[]? caps = null, bool urgent = false) =>
        new(lane.Origin, lane.Destination, lane.VehicleType, "FTL", weight, null, RequestDate, caps ?? [], urgent);

    private async Task<RecommendationResponse> RecommendAsync(TransporterSelectionRequest request)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/transporters/recommendations", request, TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RecommendationResponse>(TestHost.Json))!;
    }

    // ---------- scenarios ----------

    [Fact]
    public async Task Demo_scenario_recommends_the_service_leader_over_the_cheapest_carrier_and_explains_why()
    {
        var lane = FixtureLane.Unique();
        var abc = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("ABC Logistics", 42000m, OtdPct: 96.4m));
        var xyz = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("XYZ Transport", 39000m, OtdPct: 81.2m));
        var pqr = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("PQR Roadways", 41000m, OtdPct: 92m));
        await _host.WithTransporterDbAsync(async db =>
        {
            (await db.Transporters.SingleAsync(t => t.Id == abc)).City = "Pune";
            await db.SaveChangesAsync();
            db.PlanningRules.Add(new TransporterPlanningRule
            {
                TransporterId = abc, RuleType = PlanningRuleType.PreferredCarrier, Reason = "Best lane partner",
                EffectiveFrom = new DateTime(2026, 1, 1), IsActive = true, CreatedBy = "seed"
            });
            await db.SaveChangesAsync();
            return true;
        });

        var result = await RecommendAsync(Request(lane));

        result.Recommended!.TransporterId.Should().Be(abc);
        result.Ranked.Select(r => r.TransporterId).Should().Equal(abc, pqr, xyz);
        var xyzEntry = result.Ranked.Single(r => r.TransporterId == xyz);
        xyzEntry.Comparisons.Should().Contain(c => c.Contains("lower rate by ₹3,000"));
        xyzEntry.Comparisons.Should().Contain(c => c.Contains("On-time delivery") && c.Contains("81.2%"));
        result.Recommended.Explanations.Should().Contain(e => e.Contains("Preferred carrier"));
    }

    [Fact]
    public async Task Transporter_without_the_lane_is_ineligible_with_a_clear_reason()
    {
        var lane = FixtureLane.Unique();
        var served = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Served Co", 40000m));
        var unserved = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Unserved Co", 38000m, Lane: false));

        var response = await _admin.PostAsJsonAsync("/api/v1/transporters/eligibility/check", Request(lane), TestHost.Json);

        var candidates = (await response.Content.ReadFromJsonAsync<List<CandidateDto>>(TestHost.Json))!;
        candidates.Single(c => c.TransporterId == unserved).Reasons.Should().Contain(r => r.StartsWith("Lane not configured"));
        candidates.Single(c => c.TransporterId == unserved).Eligible.Should().BeFalse();
        candidates.Single(c => c.TransporterId == served).Eligible.Should().BeTrue();
    }

    [Fact]
    public async Task Vehicle_capacity_shortfall_is_explained_and_excludes_the_transporter()
    {
        var lane = FixtureLane.Unique();
        var small = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Small Fleet", 40000m, Capacity: 7500m));

        var result = await RecommendAsync(Request(lane, weight: 12000m));

        result.Ranked.Should().NotContain(r => r.TransporterId == small);
        var candidate = result.Candidates.Single(c => c.TransporterId == small);
        candidate.Reasons.Should().Contain("No available vehicle has capacity for 12000 kg (largest available 7500 kg).");
    }

    [Fact]
    public async Task Missing_rate_is_reported_as_an_exclusion()
    {
        var lane = FixtureLane.Unique();
        var norate = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("No Rate Co", 40000m));
        await _host.WithTransporterDbAsync(async db =>
        {
            db.TransporterRates.RemoveRange(db.TransporterRates.Where(r => r.TransporterId == norate));
            await db.SaveChangesAsync();
            return true;
        });

        var result = await RecommendAsync(Request(lane));

        result.Candidates.Single(c => c.TransporterId == norate).Reasons.Should().Contain(r => r.StartsWith("No applicable rate"));
    }

    [Fact]
    public async Task Suspended_transporter_is_not_eligible()
    {
        var lane = FixtureLane.Unique();
        var suspended = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Suspended Co", 40000m, Status: TransporterStatus.Suspended));

        var result = await RecommendAsync(Request(lane));

        result.Ranked.Should().BeEmpty();
        result.Candidates.Single(c => c.TransporterId == suspended).Reasons.Should().Contain("Transporter is suspended.");
    }

    [Fact]
    public async Task Carrier_with_insufficient_data_is_scored_neutrally_and_does_not_outrank_a_proven_carrier()
    {
        var lane = FixtureLane.Unique();
        var proven = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Proven Co", 40000m, OtdPct: 92m));
        var newcomer = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Newcomer Co", 40000m, OtdPct: 100m, OtdSample: 3m));

        var result = await RecommendAsync(Request(lane));

        result.Ranked.First().TransporterId.Should().Be(proven);
        var otd = result.Ranked.Single(r => r.TransporterId == newcomer).Components.Single(c => c.Factor == "On-time delivery");
        otd.Sufficient.Should().BeFalse();
        otd.Score.Should().Be(60m);
        result.Ranked.Single(r => r.TransporterId == newcomer).Explanations
            .Should().Contain(e => e.Contains("Not enough data for"));
    }

    [Fact]
    public async Task Required_capability_missing_excludes_the_transporter()
    {
        var lane = FixtureLane.Unique();
        await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Plain Co", 40000m));

        var result = await RecommendAsync(Request(lane, caps: ["HAZARDOUS"]));

        result.Ranked.Should().BeEmpty();
        result.Candidates.Should().OnlyContain(c => c.Reasons.Any(r => r.Contains("HAZARDOUS")));
    }

    [Fact]
    public async Task Planning_contract_returns_eligibility_rank_and_reasons_as_a_get()
    {
        var lane = FixtureLane.Unique();
        var best = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Contract Best", 40000m, OtdPct: 97m));
        var other = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Contract Other", 39000m, OtdPct: 80m));
        var url = "/api/v1/transporters/planning/eligible" +
                  $"?originLocationId={lane.Origin}&destinationLocationId={lane.Destination}&vehicleTypeId={lane.VehicleType}" +
                  $"&serviceType=FTL&requiredWeightKg=12000&requiredDate=2026-10-10";

        var response = await _admin.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profiles = (await response.Content.ReadFromJsonAsync<List<TransporterPlanningProfileDto>>(TestHost.Json))!;
        var top = profiles.Single(p => p.TransporterId == best);
        top.Eligible.Should().BeTrue();
        top.Rank.Should().Be(1);
        top.OtdPct.Should().Be(97m);
        profiles.Single(p => p.TransporterId == other).Rank.Should().Be(2);
    }

    [Fact]
    public async Task Restricted_planning_rule_excludes_the_transporter_and_is_flagged()
    {
        var lane = FixtureLane.Unique();
        var restricted = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Restricted Co", 40000m));
        var created = await _admin.PostAsJsonAsync($"/api/v1/transporters/{restricted}/planning-rules",
            new PlanningRuleRequest(PlanningRuleType.Restricted, null, "Claims above threshold", new DateTime(2026, 1, 1), null), TestHost.Json);

        var result = await RecommendAsync(Request(lane));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        result.Ranked.Should().BeEmpty();
        var candidate = result.Candidates.Single(c => c.TransporterId == restricted);
        candidate.RestrictedForPlanning.Should().BeTrue();
        candidate.Reasons.Should().Contain(r => r.Contains("Claims above threshold"));
    }

    [Fact]
    public async Task Planning_rules_need_a_manager_and_a_reason()
    {
        var lane = FixtureLane.Unique();
        var transporter = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Rule Co", 40000m));
        var url = $"/api/v1/transporters/{transporter}/planning-rules";
        var body = new PlanningRuleRequest(PlanningRuleType.PreferredCarrier, null, "Strong partner", new DateTime(2026, 1, 1), null);

        var asExecutive = await Client("Transport Executive").PostAsJsonAsync(url, body, TestHost.Json);
        var blankReason = await _admin.PostAsJsonAsync(url, body with { Reason = " " }, TestHost.Json);

        asExecutive.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        blankReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deactivated_planning_rule_no_longer_affects_eligibility()
    {
        var lane = FixtureLane.Unique();
        var transporter = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Deactivate Co", 40000m));
        var add = await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporter}/planning-rules",
            new PlanningRuleRequest(PlanningRuleType.DoNotAllocate, null, "Pending investigation", new DateTime(2026, 1, 1), null), TestHost.Json);
        var ruleId = (await add.Content.ReadFromJsonAsync<PlanningRuleDto>(TestHost.Json))!.Id;

        var before = await RecommendAsync(Request(lane));
        var deactivated = await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporter}/planning-rules/{ruleId}/deactivate",
            new ReasonRequest("Investigation closed"), TestHost.Json);
        var after = await RecommendAsync(Request(lane));

        before.Ranked.Should().BeEmpty();
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        after.Ranked.Should().ContainSingle(r => r.TransporterId == transporter);
    }
}
