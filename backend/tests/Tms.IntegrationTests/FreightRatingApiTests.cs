using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Rating;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;
using static Tms.IntegrationTests.Infrastructure.FreightApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FreightRatingApiTests(TmsApiFactory factory)
{
    private sealed record Scenario(HttpClient Admin, Guid TransporterId, Guid Truck, string State, string Region, ContractDto Contract);

    /// <summary>The contract's own worked example: Mumbai → Pune, 32 ft, 10–15 t, 151–200 km, ₹38,000, DPH on a 30% fuel share, and a toll passed through.</summary>
    private async Task<Scenario> DemoAsync(Guid? transporterId = null, decimal diesel = 102m)
    {
        var admin = await factory.AdminAsync();
        var transporter = transporterId ?? (await ActiveTransporterAsync(admin)).Id;
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var region = UniqueRegion();
        await AddDieselAsync(admin, region, diesel);
        var toll = new AccessorialSpec("TOLL", "Toll", AccessorialCalc.Reimbursed, "TRIP");
        var rate = Rate(City(state, "Mumbai"), City(state, "Pune"), new FlatTripPricing(38_000m), truck.Id, new RateExtras(Code: "RATE-MUM-PUN-32FT", MinWeightKg: 10_000m, MaxWeightKg: 15_000m), 150m, 200m);
        var contract = await BuildActiveAsync(admin, transporter, NewContract(transporter, from: Today.AddDays(-30)), [Dph(region)], [toll], [rate]);
        return new Scenario(admin, transporter, truck.Id, state, region, contract);
    }

    private static RatingRequestDto MumbaiPune(Scenario s, string? shipment = null, bool commit = false, DateOnly? date = null, decimal toll = 1_800m) =>
        Rating(s.State, "Mumbai", s.State, "Pune", vehicle: s.Truck, kg: 12_500m, km: 155m, cbm: 42m, transporter: s.TransporterId, shipment: shipment, commit: commit, date: date, inputs: new() { ["TOLL"] = toll });

    [Fact]
    public async Task The_worked_example_is_rated_at_41320_with_every_line_and_a_trace_that_explains_why()
    {
        var s = await DemoAsync();
        using var _ = s.Admin;

        var result = await CalculateAsync(s.Admin, MumbaiPune(s));

        result.Qualified.ShouldBeTrue(result.Message);
        var best = result.Selected!;
        best.BaseFreight.ShouldBe(38_000m);
        best.DphAdjustment.ShouldBe(1_520m);
        best.AccessorialAmount.ShouldBe(1_800m);
        best.TotalFreight.ShouldBe(41_320m);
        best.Lines.Select(l => (l.Type, l.Amount)).ShouldBe([("BASE_FREIGHT", 38_000m), ("DPH", 1_520m), ("TOLL", 1_800m)]);
        best.Lines.Sum(l => l.Amount).ShouldBe(best.TotalFreight);
        best.Rate.Code.ShouldBe("RATE-MUM-PUN-32FT");
        best.Rate.Version.ShouldBe(1);
        best.DphRule.ShouldBe("DPH");
        best.Reasons.ShouldContain(r => r.Contains("exact lane"));
        best.Reasons.ShouldContain(r => r.Contains("Weight slab 10000–15000"));
        best.Reasons.ShouldContain(r => r.Contains("Distance slab 150–200"));
        result.CalculationVersion.ShouldBe("1.0");
        result.Trace.Select(t => t.Stage).ShouldBe(["Input", "Contract", "Base freight", "DPH", "Accessorials", "Result"]);
        result.RatingId.ShouldBeNull("a rating that was not asked to be kept is not kept");
    }

    [Fact]
    public async Task A_kept_rating_is_reproducible_even_after_the_diesel_index_and_the_contract_change()
    {
        var s = await DemoAsync();
        using var _ = s.Admin;
        var shipment = $"SH-{Guid.NewGuid():N}"[..12];

        var kept = await CalculateAsync(s.Admin, MumbaiPune(s, shipment, commit: true));

        kept.Committed.ShouldBeTrue();
        kept.RatingReference.ShouldStartWith("FR-");
        kept.Selected!.TotalFreight.ShouldBe(41_320m);

        // diesel moves, and the contract is revised with a dearer rate from tomorrow
        await AddDieselAsync(s.Admin, s.Region, 130m, Today.AddDays(-1));
        var revised = await (await s.Admin.PostJsonAsync($"/api/v1/contracts/{s.Contract.Summary.Id}/revise", new ReviseRequest(Today.AddDays(1), Today.AddYears(1)))).ReadAsync<ContractDto>();
        var rates = await (await s.Admin.GetAsync($"/api/v1/contracts/{revised.Summary.Id}/rates")).ReadAsync<List<RateCardDto>>();
        var dearer = rates.Select(r => new RateInputDto(r.Origin, r.Destination, r.BothWays, r.VehicleTypeId, r.MinDistanceKm, r.MaxDistanceKm, new FlatTripPricing(45_000m), r.Extras)).ToArray();
        revised = await WithRatesAsync(s.Admin, revised, dearer);
        await SubmitAsync(s.Admin, revised.Summary.Id);

        var stored = await (await s.Admin.GetAsync($"/api/v1/freight-rating/{kept.RatingId}")).ReadAsync<RatingDetailDto>();
        stored.Summary.TotalFreight.ShouldBe(41_320m);
        stored.Summary.ContractRevision.ShouldBe(1);
        stored.Result.Selected!.Lines.Select(l => l.Amount).ShouldBe([38_000m, 1_520m, 1_800m]);
        stored.Request!.ShipmentReference.ShouldBe(shipment);

        var again = await (await s.Admin.GetAsync($"/api/v1/freight-rating/{kept.RatingId}/reproduce")).ReadAsync<ReproduceDto>();
        again.Matches.ShouldBeTrue(string.Join("; ", again.Differences));
        again.Recalculated.ShouldBeTrue();

        // a shipment tomorrow is rated on the new diesel price (and the revised rate); the kept one is not touched
        var tomorrow = await CalculateAsync(s.Admin, MumbaiPune(s, date: Today.AddDays(1)));
        tomorrow.Selected!.DphAdjustment.ShouldBeGreaterThan(1_520m);
        tomorrow.Selected.BaseFreight.ShouldBe(45_000m);
        // and the day the kept rating used stays fixed at the diesel price it used, whatever the index says now
        (await CalculateAsync(s.Admin, MumbaiPune(s))).Selected!.DphAdjustment.ShouldBe(1_520m);
        var history = await (await s.Admin.GetAsync($"/api/v1/freight-rating/history?shipmentReference={shipment}")).ReadAsync<PagedResult<RatingSummaryDto>>();
        history.Items.ShouldHaveSingleItem().TotalFreight.ShouldBe(41_320m);
    }

    [Fact]
    public async Task A_contract_version_prices_the_shipments_of_its_own_dates()
    {
        var s = await DemoAsync();
        using var _ = s.Admin;
        var v2 = await (await s.Admin.PostJsonAsync($"/api/v1/contracts/{s.Contract.Summary.Id}/revise", new ReviseRequest(Today.AddDays(10), Today.AddYears(1)))).ReadAsync<ContractDto>();
        var rates = await (await s.Admin.GetAsync($"/api/v1/contracts/{v2.Summary.Id}/rates")).ReadAsync<List<RateCardDto>>();
        v2 = await WithRatesAsync(s.Admin, v2, rates.Select(r => new RateInputDto(r.Origin, r.Destination, r.BothWays, r.VehicleTypeId, r.MinDistanceKm, r.MaxDistanceKm, new FlatTripPricing(40_000m), r.Extras)).ToArray());
        await SubmitAsync(s.Admin, v2.Summary.Id);

        var before = await CalculateAsync(s.Admin, MumbaiPune(s, date: Today.AddDays(5)));
        var after = await CalculateAsync(s.Admin, MumbaiPune(s, date: Today.AddDays(15)));

        before.Selected!.BaseFreight.ShouldBe(38_000m);
        before.Selected.ContractRevision.ShouldBe(1);
        after.Selected!.BaseFreight.ShouldBe(40_000m);
        after.Selected.ContractRevision.ShouldBe(2);
        after.Selected.Rate.Version.ShouldBe(2, "the rate changed, so its version moved up");
    }

    [Fact]
    public async Task No_rate_is_a_clear_answer_and_the_miss_is_counted_as_an_uncovered_lane()
    {
        var s = await DemoAsync();
        using var _ = s.Admin;
        var lane = Rating(s.State, "Mumbai", s.State, "Nagpur", vehicle: s.Truck, kg: 12_500m, km: 800m, transporter: s.TransporterId);

        var result = await CalculateAsync(s.Admin, lane);

        result.Qualified.ShouldBeFalse();
        result.ErrorCode.ShouldBe("FREIGHT_RATE_NOT_FOUND");
        result.Message!.ShouldContain("No applicable active freight rate");
        result.Advice.ShouldNotBeEmpty();
        result.Selected.ShouldBeNull();

        var failed = await (await s.Admin.GetAsync("/api/v1/freight-rating/history?qualified=false&pageSize=50")).ReadAsync<PagedResult<RatingSummaryDto>>();
        failed.Items.ShouldContain(r => r.Lane.Contains("Nagpur", StringComparison.OrdinalIgnoreCase) && !r.Qualified);
        var coverage = await (await s.Admin.GetAsync("/api/v1/freight-contract-dashboard/rate-coverage")).ReadAsync<RateCoverageDto>();
        coverage.Uncovered.ShouldContain(l => l.Lane.Contains("Nagpur", StringComparison.OrdinalIgnoreCase));
        coverage.LoadsWithoutRate.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Overlapping_rates_block_approval_until_a_priority_or_a_slab_separates_them_and_the_priority_then_decides()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        await NoApprovalNeededAsync(admin);
        RateInputDto Banded(string code, decimal amount, decimal from, decimal to, int priority = 100) =>
            Rate(City(state, "A"), City(state, "B"), new FlatTripPricing(amount), truck.Id, new RateExtras(Code: code, MinWeightKg: from, MaxWeightKg: to, Priority: priority));
        var draft = await WithRatesAsync(admin, await CreateAsync(admin, NewContract(transporter.Id)), Banded("LOW", 38_000m, 5_000m, 12_000m), Banded("HIGH", 41_000m, 10_000m, 18_000m));

        var blocked = await admin.PostAsync($"/api/v1/contracts/{draft.Summary.Id}/submit", null);
        blocked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).ShouldContain("Rate conflict");
        (await GetAsync(admin, draft.Summary.Id)).ValidationErrors.ShouldBeGreaterThan(0);

        var fixedUp = await WithRatesAsync(admin, draft, Banded("LOW", 38_000m, 5_000m, 12_000m, priority: 10), Banded("HIGH", 41_000m, 10_000m, 18_000m));
        fixedUp.ValidationErrors.ShouldBe(0);
        fixedUp.ValidationWarnings.ShouldBeGreaterThan(0);
        (await SubmitAsync(admin, fixedUp.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.Active);

        var both = await CalculateAsync(admin, Rating(state, "A", state, "B", vehicle: truck.Id, kg: 11_000m, transporter: transporter.Id));
        both.Selected!.Rate.Code.ShouldBe("LOW");
        both.Exclusions.ShouldContain(e => e.ReasonCode == "LOWER_PRIORITY");
        var only = await CalculateAsync(admin, Rating(state, "A", state, "B", vehicle: truck.Id, kg: 16_000m, transporter: transporter.Id));
        only.Selected!.Rate.Code.ShouldBe("HIGH");
    }

    [Fact]
    public async Task When_there_is_no_exact_lane_the_zone_rate_is_used_and_the_result_says_so()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var (north, south) = (UniqueState(), UniqueState());
        var zone = $"Z{Guid.NewGuid():N}"[..10];
        (await admin.PostJsonAsync("/api/v1/zones", new SaveZoneRequest(zone, "Test zone", [new ZoneMember(north, null), new ZoneMember(south, null)], null))).StatusCode.ShouldBe(HttpStatusCode.Created);
        await BuildActiveAsync(admin, transporter.Id, rates: [Rate(Zone(zone), Zone(zone), new FlatTripPricing(42_000m), truck.Id)]);

        var result = await CalculateAsync(admin, Rating(north, "X", south, "Y", vehicle: truck.Id, km: 400m, transporter: transporter.Id));

        result.Selected!.BaseFreight.ShouldBe(42_000m);
        result.Selected.Reasons.ShouldContain(r => r.Contains("zone"));
    }

    [Fact]
    public async Task Simulating_keeps_nothing_and_what_if_and_comparison_answer_side_by_side()
    {
        using var admin = await factory.AdminAsync();
        var (a, b) = (await ActiveTransporterAsync(admin), await ActiveTransporterAsync(admin));
        var truck = await VehicleTypeAsync(admin);
        var small = (await (await admin.GetAsync("/api/v1/vehicle-types")).ReadAsync<List<Tms.Modules.Transporters.Application.VehicleTypeDto>>()).First(t => t.Code != "TRUCK_32FT_MXL");
        var state = UniqueState();
        RateInputDto Slabs(decimal ratePerKg) => Rate(State(state), State(state), new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Kg, SlabMethod.Flat, [new Slab(0, 500, ratePerKg), new Slab(500, null, ratePerKg - 1)]));
        await BuildActiveAsync(admin, a.Id, NewContract(a.Id, ContractType.Ptl), rates: [Slabs(10m)]);
        await BuildActiveAsync(admin, b.Id, NewContract(b.Id, ContractType.Ptl), rates: [Slabs(11m)]);
        var request = Rating(state, "P", state, "Q", ContractType.Ptl, kg: 400m);

        var before = (await (await admin.GetAsync("/api/v1/freight-rating/history?pageSize=1")).ReadAsync<PagedResult<RatingSummaryDto>>()).TotalCount;
        var simulated = await CalculateAsync(admin, request, "simulate");
        var qualified = await CalculateAsync(admin, request, "qualify");

        simulated.RatingId.ShouldBeNull();
        qualified.Options.Select(o => o.TotalFreight).ShouldBe([4_000m, 4_400m]);
        (await (await admin.GetAsync("/api/v1/freight-rating/history?pageSize=1")).ReadAsync<PagedResult<RatingSummaryDto>>()).TotalCount.ShouldBe(before, "simulation and qualification leave no trace");

        var whatIf = await (await admin.PostJsonAsync("/api/v1/freight-rating/what-if", new WhatIfRequest(request with { TransporterId = a.Id },
            [new WhatIfVariation("Heavier", WeightKg: 800m), new WhatIfVariation("Lighter", WeightKg: 200m)]))).ReadAsync<WhatIfResultDto>();
        whatIf.Base.TotalFreight.ShouldBe(4_000m);
        whatIf.Variations.Single(v => v.Label == "Heavier").TotalFreight.ShouldBe(7_200m);   // 800 kg x 9
        whatIf.Variations.Single(v => v.Label == "Lighter").DifferenceFromBase.ShouldBe(-2_000m);

        var compared = await (await admin.PostJsonAsync("/api/v1/freight-rating/compare", new CompareRequest(request, [a.Id, b.Id]))).ReadAsync<List<CompareRowDto>>();
        compared.Select(c => c.Option!.TotalFreight).ShouldBe([4_000m, 4_400m]);
        compared[0].TransporterId.ShouldBe(a.Id);
        _ = truck;
        _ = small;
    }

    [Fact]
    public async Task An_override_keeps_both_figures_needs_a_reason_and_the_right_permission()
    {
        var s = await DemoAsync();
        using var _ = s.Admin;
        using var plain = await factory.UserWithPermissionsAsync(s.Admin, ContractPermissions.Read);
        var kept = await CalculateAsync(s.Admin, MumbaiPune(s, $"SH-{Guid.NewGuid():N}"[..12], commit: true));

        (await plain.PostJsonAsync($"/api/v1/freight-rating/{kept.RatingId}/override", new OverrideRatingRequest(42_000m, "Special commercial approval", "Commercial Manager"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Admin.PostJsonAsync($"/api/v1/freight-rating/{kept.RatingId}/override", new OverrideRatingRequest(42_000m, "", null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var overridden = await (await s.Admin.PostJsonAsync($"/api/v1/freight-rating/{kept.RatingId}/override", new OverrideRatingRequest(42_000m, "Special commercial approval", "Commercial Manager"))).ReadAsync<RatingDetailDto>();

        overridden.OverrideAmount.ShouldBe(42_000m);
        overridden.Summary.TotalFreight.ShouldBe(41_320m, "the calculated freight is never replaced");
        overridden.OverrideReason.ShouldBe("Special commercial approval");
        overridden.OverrideApprovedBy.ShouldBe("Commercial Manager");
        (await (await s.Admin.GetAsync($"/api/v1/freight-rating/{kept.RatingId}/reproduce")).ReadAsync<ReproduceDto>()).Matches.ShouldBeTrue();
        (await s.Admin.DeleteAsync($"/api/v1/freight-rating/{kept.RatingId}/override")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Admin.PostJsonAsync("/api/v1/freight-rating/calculate", MumbaiPune(s) with { Commit = true, ShipmentReference = null })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Planning_audit_transporter_and_capacity_integrations_work_through_the_shared_contracts()
    {
        var s = await DemoAsync();
        using var _ = s.Admin;
        var shipment = $"SH-{Guid.NewGuid():N}"[..12];
        var tenant = await TenantIdAsync();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Tms.SharedKernel.Security.IAmbientUserContext>().RunAs(tenant, null, "test");
        var planning = scope.ServiceProvider.GetRequiredService<IFreightPlanningIntegration>();
        var audit = scope.ServiceProvider.GetRequiredService<IContractualBaselineService>();
        var coverage = scope.ServiceProvider.GetRequiredService<IFreightTransporterIntegration>();
        var request = new FreightRatingRequest(Today, s.State, "Mumbai", s.State, "Pune", FreightServiceType.Ftl, s.TransporterId, s.Truck, 12_500m, 42m, 155m, 1, null, new Dictionary<string, decimal> { ["TOLL"] = 1_800m }, shipment);

        var estimate = await planning.CalculatePlanningFreightAsync(request);
        estimate.Qualified.ShouldBeTrue();
        estimate.TotalFreight.ShouldBe(41_320m);
        estimate.RatingId.ShouldBeNull("planning estimates are not kept");
        (await planning.GetFreightOptionsAsync(request)).Options.ShouldHaveSingleItem().ContractReference.ShouldBe(s.Contract.Summary.Number);

        (await audit.GetContractualBaselineAsync(new FreightAuditRequest(shipment, null))).Found.ShouldBeFalse();
        var kept = await planning.CalculatePlanningFreightAsync(request with { Commit = true });
        kept.RatingId.ShouldNotBeNull();
        var baseline = await audit.GetContractualBaselineAsync(new FreightAuditRequest(shipment, null));
        baseline.Found.ShouldBeTrue();
        baseline.FromCommittedRating.ShouldBeTrue();
        (baseline.ExpectedBaseFreight, baseline.ExpectedDph, baseline.ExpectedAccessorials, baseline.ExpectedTotal).ShouldBe((38_000m, 1_520m, 1_800m, 41_320m));
        (await audit.GetContractualBaselineAsync(new FreightAuditRequest(null, request))).FromCommittedRating.ShouldBeFalse();

        (await coverage.HasActiveCommercialCoverageAsync(s.TransporterId, s.State, "Mumbai", s.State, "Pune", FreightServiceType.Ftl)).ShouldBeTrue();
        (await coverage.HasActiveCommercialCoverageAsync(s.TransporterId, s.State, "Mumbai", s.State, "Nagpur", FreightServiceType.Ftl)).ShouldBeFalse();
        (await coverage.HasActiveCommercialCoverageAsync(Guid.NewGuid(), s.State, "Mumbai", s.State, "Pune", FreightServiceType.Ftl)).ShouldBeFalse();
    }

    [Fact]
    public async Task Committed_capacity_and_service_levels_are_exposed_to_planning_once_the_contract_is_active()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        await NoApprovalNeededAsync(admin);
        var contract = await CreateAsync(admin, NewContract(transporter.Id));
        contract = await (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/capacity", new SaveCapacityRequest([new CapacitySpec(truck.Id, 6, 18_000m, 250, null, 60m, null, null)], contract.Version))).ReadAsync<ContractDto>();
        contract = await (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/sla", new SaveSlaRequest([new SlaSpec(ContractType.Ftl, Place.OfState(state), Place.OfState(state), null, 2160, null, 360, null, null)], contract.Version))).ReadAsync<ContractDto>();
        contract = await WithRatesAsync(admin, contract, Flat(State(state), State(state), 40_000m, truck.Id));
        await SubmitAsync(admin, contract.Summary.Id);
        var tenant = await TenantIdAsync();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Tms.SharedKernel.Security.IAmbientUserContext>().RunAs(tenant, null, "test");
        var provider = scope.ServiceProvider.GetRequiredService<IContractedCapacityProvider>();

        var capacity = (await provider.GetCapacityAsync(transporter.Id, Today)).ShouldHaveSingleItem();
        capacity.CommittedVehicleCount.ShouldBe(6);
        capacity.MinimumMonthlyTrips.ShouldBe(250);
        capacity.TargetBusinessSharePct.ShouldBe(60m);
        capacity.VehicleTypeName.ShouldNotBeNull();
        (await provider.GetSlaAsync(transporter.Id, Today)).ShouldHaveSingleItem().TransitSlaMinutes.ShouldBe(2160);
        (await provider.GetCapacityAsync(transporter.Id, Today.AddYears(5))).ShouldBeEmpty();

        var rated = await CalculateAsync(admin, Rating(state, "A", state, "B", vehicle: truck.Id, km: 100m, transporter: transporter.Id));
        rated.Selected!.TransitSlaMinutes.ShouldBe(2160);
    }

    private async Task<Guid> TenantIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<Tms.Modules.Platform.Infrastructure.Persistence.PlatformDbContext>().Tenants.FirstAsyncOrDefaultAsync(TmsApiFactory.DemoTenant)).Id;
    }
}

internal static class TenantLookup
{
    public static Task<Tms.Modules.Platform.Domain.Tenant> FirstAsyncOrDefaultAsync(this IQueryable<Tms.Modules.Platform.Domain.Tenant> tenants, string code) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(tenants, t => t.Code == code);
}
