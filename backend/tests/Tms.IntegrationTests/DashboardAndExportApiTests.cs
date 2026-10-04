using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DashboardAndExportApiTests(TmsApiFactory factory)
{
    private static async Task<DashboardDto> DashboardAsync(HttpClient client, DateOnly from, DateOnly to)
    {
        var response = await client.GetAsync($"/api/v1/planning/dashboard?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<DashboardDto>();
    }

    [Fact]
    public async Task The_dashboard_reflects_new_plans_counts_each_plan_once_and_ignores_cancelled_ones()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var day = ContractApiData.Today.AddDays(40); // a quiet window other tests do not touch
        var before = await DashboardAsync(s.Admin, day, day);

        var order = await s.OrderAsync(6_000m);
        var created = await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(day, [order.Id], new PlanOptions(AllowPtl: false)));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var run = await created.ReadAsync<RunDto>();

        var after = await DashboardAsync(s.Admin, day, day);
        after.Kpis.Plans.ShouldBe(before.Kpis.Plans + 1);
        after.Kpis.TotalFreightCost.ShouldBe(before.Kpis.TotalFreightCost + s.FlatRate);
        after.Kpis.OrdersPlanned.ShouldBe(before.Kpis.OrdersPlanned + 1);
        after.Kpis.AverageWeightUtilisation.ShouldNotBeNull();
        after.Kpis.Daily.Single(d => d.Date == day).Plans.ShouldBe(after.Kpis.Plans);

        // A re-plan adds a version but the plan is still counted once.
        await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/reoptimize", new ReoptimizeRequest("Try again", null));
        (await DashboardAsync(s.Admin, day, day)).Kpis.Plans.ShouldBe(after.Kpis.Plans);

        var latest = await (await s.Admin.GetAsync("/api/v1/planning/runs?pageSize=50")).Content.ReadAsStringAsync();
        latest.ShouldContain(run.Number);
    }

    [Fact]
    public async Task A_cancelled_plan_drops_out_of_the_dashboard()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var day = ContractApiData.Today.AddDays(60);
        var before = await DashboardAsync(s.Admin, day, day);
        var order = await s.OrderAsync(2_000m);
        var run = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(day, [order.Id], new PlanOptions(AllowPtl: false)))).ReadAsync<RunDto>();
        (await DashboardAsync(s.Admin, day, day)).Kpis.Plans.ShouldBe(before.Kpis.Plans + 1);

        await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/cancel", new ReasonRequest("not needed"));

        (await DashboardAsync(s.Admin, day, day)).Kpis.Plans.ShouldBe(before.Kpis.Plans);
    }

    [Fact]
    public async Task The_period_is_validated_and_access_is_staff_only()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var reader = await factory.UserWithPermissionsAsync(s.Admin, "shipments.read");
        var today = ContractApiData.Today;

        (await s.Admin.GetAsync($"/api/v1/planning/dashboard?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync($"/api/v1/planning/dashboard?from={today.AddDays(-400):yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reader.GetAsync("/api/v1/planning/dashboard")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Vendor.GetAsync("/api/v1/planning/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.GetAsync("/api/v1/planning/dashboard/export?format=csv")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_plan_exports_to_csv_and_excel_as_downloads_and_unknown_formats_are_refused()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var order = await s.OrderAsync(3_000m);
        var run = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [order.Id], new PlanOptions(AllowPtl: false)))).ReadAsync<RunDto>();

        var csv = await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/export?format=csv");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        csv.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().ShouldContain(run.Number);
        (await csv.Content.ReadAsStringAsync()).ShouldContain(order.Number);

        var xlsx = await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/export?format=xlsx");
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await xlsx.Content.ReadAsByteArrayAsync())[..2].ShouldBe([(byte)'P', (byte)'K']);

        var pdf = await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/export?format=pdf");
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var pdfText = System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync());
        pdfText.ShouldStartWith("%PDF-1.4");
        pdfText.ShouldContain(order.Number);
        pdfText.ShouldContain("(Vehicles) Tj");
        (await s.Admin.GetAsync("/api/v1/planning/dashboard/export?format=pdf")).Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/export?format=docx")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync($"/api/v1/planning/runs/{Guid.NewGuid()}/export?format=csv")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        (await acme.GetAsync($"/api/v1/planning/runs/{run.Id}/export?format=csv")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var dashboard = await s.Admin.GetAsync("/api/v1/planning/dashboard/export?format=xlsx");
        dashboard.StatusCode.ShouldBe(HttpStatusCode.OK);
        dashboard.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull().ShouldStartWith("planning-kpis-");
    }
}
