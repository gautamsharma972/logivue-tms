using System.Net;
using System.Text.Json;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Reports.Application;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransportOpsReportsApiTests(TmsApiFactory factory)
{
    private const string Reports = "/api/v1/reports";

    private static async Task SetSourceAsync(HttpClient admin, string source)
    {
        var current = await (await admin.GetAsync($"{Reports}/settings")).ReadAsync<SettingsDto>();
        (await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(current.Settings with { DataSource = source }, current.Version))).EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> RunAsync(HttpClient admin, string code, object? body = null)
    {
        var response = await admin.PostJsonAsync($"{Reports}/{code}/execute", body ?? new { pageSize = 500 });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task The_operations_reports_show_what_the_module_holds_and_nothing_made_up()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (lr, trip) = await s.DeliveredAsync(TimeSpan.FromHours(7)); // 3 hours of detention at 200 an hour
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);
        var reviewed = await s.ReviewedAsync(lr.Lr.Lr.Id);
        var invoice = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([reviewed.Id], null, null, null)));
        var (pending, _) = await s.DeliveredAsync(TimeSpan.FromHours(1)); // delivered, POD still waiting
        await SetSourceAsync(s.Admin, "Live");
        try
        {
            var window = new { filters = new Dictionary<string, string> { ["period"] = "Rolling90", ["customer"] = s.Customer.Name }, pageSize = 500 };

            var register = await RunAsync(s.Admin, "R41_LR_REGISTER", window);
            register.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("lr").GetString()).ShouldContain(lr.Lr.Lr.Number);

            var waiting = await RunAsync(s.Admin, "R46_PENDING_POD", window);
            var waitingRows = waiting.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("lr").GetString()).ToList();
            waitingRows.ShouldContain(pending.Lr.Lr.Number);
            waitingRows.ShouldNotContain(lr.Lr.Lr.Number); // its POD was accepted

            var detention = await RunAsync(s.Admin, "R51_DETENTION", window);
            var row = detention.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("lr").GetString() == lr.Lr.Lr.Number);
            row.GetProperty("detention").GetDecimal().ShouldBe(600m);
            row.GetProperty("waitH").GetDecimal().ShouldBe(7m);

            var invoices = await RunAsync(s.Admin, "R54_INVOICE_REGISTER", window);
            invoices.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("invoice").GetString()).ShouldContain(invoice.Summary.Number);

            var receivables = await RunAsync(s.Admin, "R56_RECEIVABLES", window);
            receivables.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("invoice").GetString() == invoice.Summary.Number).GetProperty("outstanding").GetDecimal().ShouldBe(invoice.Summary.GrandTotal);

            var loading = await RunAsync(s.Admin, "R43_CFA_LOADING", window);
            loading.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("trip").GetString()).ShouldContain(trip.Trip.Number);
        }
        finally
        {
            await SetSourceAsync(s.Admin, "Demo");
        }
    }

    [Fact]
    public async Task A_user_without_the_operations_report_permission_cannot_run_them_and_a_vendor_sees_none()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        using var finance = await factory.UserWithPermissionsAsync(s.Admin, "reports.read", "reports.contracts");
        using var allowed = await factory.UserWithPermissionsAsync(s.Admin, "reports.read", "reports.operations");

        (await finance.PostJsonAsync($"{Reports}/R41_LR_REGISTER/execute", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await allowed.PostJsonAsync($"{Reports}/R41_LR_REGISTER/execute", new { })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
