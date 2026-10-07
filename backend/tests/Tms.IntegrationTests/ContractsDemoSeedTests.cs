using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;
using static Tms.IntegrationTests.Infrastructure.FreightApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ContractsDemoSeedTests(TmsApiFactory factory)
{
    private sealed record Carrier(Guid Id, string Name);

    private sealed record SeedBody(List<Carrier> Carriers);

    /// <summary>The demo uses ordinary state names, so it is removed again: other tests price those lanes.</summary>
    private async Task CleanUpAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Ratings.IgnoreQueryFilters().Where(r => r.Reference.StartsWith("FR-009")).ExecuteDeleteAsync();
        await db.Contracts.IgnoreQueryFilters().Where(c => c.Number.StartsWith("CNT-")).ExecuteDeleteAsync();
        await db.DieselPrices.IgnoreQueryFilters().Where(d => d.Source == "Demo index").ExecuteDeleteAsync();
        await db.Zones.IgnoreQueryFilters().Where(z => z.Code == "WEST" || z.Code == "NORTH" || z.Code == "SOUTH" || z.Code == "EAST").ExecuteDeleteAsync();
    }

    [Fact]
    public async Task The_demo_book_has_the_contracts_rates_dph_and_history_the_scenarios_need_and_runs_only_once()
    {
        using var admin = await factory.AdminAsync();
        var carriers = new List<Carrier>();
        for (var i = 0; i < 3; i++)
        {
            var t = await ActiveTransporterAsync(admin);
            carriers.Add(new Carrier(t.Id, t.LegalName));
        }

        try
        {
            var response = await admin.PostJsonAsync("/api/v1/dev/contracts/seed-demo", new SeedBody(carriers));
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var seeded = await response.ReadAsync<ContractsDemoResult>();
            seeded.Contracts.ShouldBeGreaterThanOrEqualTo(20);
            seeded.Rates.ShouldBeGreaterThanOrEqualTo(150);
            seeded.DphRules.ShouldBeGreaterThanOrEqualTo(10);
            seeded.Ratings.ShouldBeGreaterThan(30);
            seeded.Documents.ShouldBeGreaterThan(0);

            var all = await (await admin.GetAsync("/api/v1/freight-rates?pageSize=500&search=RATE-")).ReadAsync<PagedResult<RateRowDto>>();
            all.TotalCount.ShouldBeGreaterThan(100);
            (await (await admin.GetAsync("/api/v1/freight-rates?pageSize=1&search=RATE-ZONE")).ReadAsync<PagedResult<RateRowDto>>()).TotalCount.ShouldBeGreaterThanOrEqualTo(50);

            // all three services, an expired contract, expiring ones, a suspended one and a draft with a clash
            var contracts = (await (await admin.GetAsync("/api/v1/contracts?search=CNT-&pageSize=100")).ReadAsync<PagedResult<ContractSummaryDto>>()).Items;
            contracts.Select(c => c.Type).Distinct().Order().ShouldBe([ContractType.Ftl, ContractType.Ptl, ContractType.Dedicated]);
            contracts.ShouldContain(c => c.Status == ContractStatus.Expired);
            contracts.ShouldContain(c => c.Status == ContractStatus.Suspended);
            contracts.Any(c => c.DaysUntilExpiry is >= 0 and <= 30).ShouldBeTrue();
            contracts.Where(c => c.Number == "CNT-ABC-2026").Select(c => c.Revision).Order().ShouldBe([1, 2, 3]);

            // the worked example rates at 41,320 today, and the contract's own history says 39,800 on 10 July
            var today = await CalculateAsync(admin, Rating("Maharashtra", "Mumbai", "Maharashtra", "Pune", vehicle: (await VehicleTypeAsync(admin)).Id, kg: 12_500m, km: 155m, inputs: new() { ["TOLL"] = 1_800m }), "simulate");
            today.Selected!.ContractReference.ShouldBe("CNT-ABC-2026");
            today.Selected.TotalFreight.ShouldBe(41_320m);

            var history = await (await admin.GetAsync("/api/v1/freight-rating/history?shipmentReference=SH-10025")).ReadAsync<PagedResult<RatingSummaryDto>>();
            var kept = history.Items.ShouldHaveSingleItem();
            (kept.ContractReference, kept.ContractRevision, kept.RateVersion, kept.TotalFreight).ShouldBe(("CNT-ABC-2026", 2, 7, 39_800m));
            var detail = await (await admin.GetAsync($"/api/v1/freight-rating/{kept.Id}")).ReadAsync<RatingDetailDto>();
            detail.DphRule.ShouldBe("DPH");
            detail.DphVersion.ShouldBe(4);
            (await (await admin.GetAsync($"/api/v1/freight-rating/{kept.Id}/reproduce")).ReadAsync<ReproduceDto>()).Matches.ShouldBeTrue();

            // validation examples and the fallback and DPH demonstrations
            var conflict = contracts.Single(c => c.Number == "CNT-DEMO-CONFLICT");
            (await admin.PostAsync($"/api/v1/contracts/{conflict.Id}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var fallback = await CalculateAsync(admin, Rating("Delhi", "New Delhi", "Telangana", "Hyderabad", vehicle: (await VehicleTypeAsync(admin)).Id, km: 1_550m), "simulate");
            fallback.Selected!.Rate.Code.ShouldBe("RATE-NORTH-SOUTH");
            fallback.Selected.Reasons.ShouldContain(r => r.Contains("zone"));
            var dph = await (await admin.GetAsync("/api/v1/dph/rules?inForceOnly=true")).ReadAsync<List<DphOverviewDto>>();
            dph.Single(d => d.Rule.ContractNumber == "CNT-DEMO-DPH").AdjustmentPercent.ShouldBe(3m);

            (await (await admin.PostJsonAsync("/api/v1/dev/contracts/seed-demo", new SeedBody(carriers))).ReadAsync<ContractsDemoResult>()).Contracts.ShouldBe(0, "a second run adds nothing");
        }
        finally
        {
            await CleanUpAsync();
        }
    }
}
