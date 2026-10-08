using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Reports.Application;
using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReportsApiTests(TmsApiFactory factory)
{
    private const string Reports = "/api/v1/reports";

    private static readonly string[] ByTransporter = ["transporter"];

    private static readonly string[] Nonsense = ["nonsense"];

    private static readonly string[] Trends = ["up", "down", "flat"];

    private static readonly string[] AuditFields = ["shipment", "contract", "contractVersion", "rateCode", "rateVersion", "baseFreight", "dph", "accessorials", "finalFreight", "calculationVersion"];

    private static readonly (string Format, string Signature)[] Formats = [("csv", "\uFEFFDelivery"), ("xlsx", "PK"), ("pdf", "%PDF-1.4")];

    /// <summary>The demonstration dataset is the same on every run, so numbers can be asserted; it is chosen per organisation in the report settings.</summary>
    private async Task<HttpClient> AdminOnDemoAsync(int syncExportRows = 5_000)
    {
        var admin = await factory.AdminAsync();
        var current = await (await admin.GetAsync($"{Reports}/settings")).ReadAsync<SettingsDto>();
        var put = await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(current.Settings with { DataSource = "Demo", SyncExportRows = syncExportRows }, current.Version));
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        return admin;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> RunAsync(HttpClient client, string code, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostJsonAsync($"{Reports}/{code}/execute", body ?? new { });
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static Dictionary<string, JsonElement> Filters(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

    private async Task<HttpClient> VendorAsync(HttpClient admin, Guid transporterId)
    {
        var role = await admin.CreateExternalRoleAsync(ReportingPermissions.Self);
        var email = ApiExtensions.UniqueEmail("vendor-reports");
        (await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(email, "Vendor Reporter", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporterId))).StatusCode.ShouldBe(HttpStatusCode.Created);
        return await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);
    }

    private static async Task<ReportJobDto> WaitForAsync(HttpClient client, Guid jobId, params ReportJobStatus[] wanted)
    {
        for (var i = 0; i < 60; i++)
        {
            var job = await (await client.GetAsync($"{Reports}/jobs/{jobId}")).ReadAsync<ReportJobDto>();
            if (wanted.Any(w => w.ToString() == job.Status))
            {
                return job;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Job {jobId} did not reach {string.Join('/', wanted)}.");
    }

    // ---- framework

    [Fact]
    public async Task The_catalogue_lists_all_39_reports_and_each_one_publishes_its_columns_filters_grouping_sorting_and_export_formats()
    {
        using var admin = await AdminOnDemoAsync();
        var list = await (await admin.GetAsync(Reports)).ReadAsync<List<ReportSummaryDto>>();
        list.Count.ShouldBe(39);
        list.Select(r => r.Category).Distinct().Count().ShouldBe(7);

        var found = await (await admin.GetAsync($"{Reports}?search=transporter")).ReadAsync<List<ReportSummaryDto>>();
        found.Select(r => r.ReportCode).ShouldContain("R09_TRANSPORTER_SCORECARD");
        found.Select(r => r.ReportCode).ShouldContain("R12_TENDER_PERFORMANCE");

        var meta = await (await admin.GetAsync($"{Reports}/R10_TRANSPORTER_RANKING/metadata")).ReadAsync<ReportMetadataDto>();
        meta.Columns.Select(c => c.Field).ShouldContain("rank");
        meta.Filters.Select(f => f.Name).ShouldContain("rankBy");
        meta.Filters.Single(f => f.Name == "transporter" || f.Name == "lane").LookupSource.ShouldNotBeNull();
        meta.ExportFormats.ShouldContain("pdf");
        meta.CanExport.ShouldBeTrue();
        meta.CanSchedule.ShouldBeTrue();

        var grouped = await (await admin.GetAsync($"{Reports}/R17_DELIVERY_PERFORMANCE/metadata")).ReadAsync<ReportMetadataDto>();
        grouped.AvailableGrouping.Select(g => g.Field).ShouldBe(["transporter", "customer", "lane", "status", "month"]);
        grouped.Sorting.ShouldNotBeEmpty();
        grouped.Drills.ShouldNotBeEmpty();
        (await admin.GetAsync($"{Reports}/NO_SUCH_REPORT/metadata")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Server_side_paging_sorting_filtering_and_grouping_return_the_requested_slice_and_the_true_total()
    {
        using var admin = await AdminOnDemoAsync();
        var window = Filters(("period", "Rolling90"));

        var all = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = window, page = 1, pageSize = 500, sort = new[] { new { field = "completedAt", direction = "DESC" } } });
        var total = all.GetProperty("totalRows").GetInt32();
        total.ShouldBeGreaterThan(300);
        all.GetProperty("rows").GetArrayLength().ShouldBe(500);

        var page = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = window, page = 3, pageSize = 25, sort = new[] { new { field = "completedAt", direction = "DESC" } } });
        page.GetProperty("totalRows").GetInt32().ShouldBe(total);
        page.GetProperty("page").GetInt32().ShouldBe(3);
        page.GetProperty("rows").GetArrayLength().ShouldBe(25);
        page.GetProperty("rows")[0].GetProperty("delivery").GetString().ShouldBe(all.GetProperty("rows")[50].GetProperty("delivery").GetString(), "page 3 of 25 starts at row 51 of the same ordering");

        var ascending = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = window, pageSize = 5, sort = new[] { new { field = "delayMin", direction = "ASC" } } });
        var delays = ascending.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("delayMin")).Where(d => d.ValueKind == JsonValueKind.Number).Select(d => d.GetDecimal()).ToList();
        delays.ShouldBe(delays.Order().ToList());

        var byTransporter = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = window, groupBy = ByTransporter, pageSize = 100 });
        byTransporter.GetProperty("groupedBy")[0].GetString().ShouldBe("transporter");
        byTransporter.GetProperty("totalRows").GetInt32().ShouldBe(22);
        var sumDeliveries = byTransporter.GetProperty("rows").EnumerateArray().Sum(r => r.GetProperty("deliveries").GetInt32());
        sumDeliveries.ShouldBe(total, "a grouped report accounts for every row");
        byTransporter.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("field").GetString()).ShouldContain("otd");

        var one = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("period", "Rolling90"), ("transporter", "Safexpress Carriers")), pageSize = 500 });
        one.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("transporter").GetString()).Distinct().ShouldBe(["Safexpress Carriers"]);

        var text = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("period", "Rolling90"), ("f_status", "refus")), pageSize = 500 });
        text.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("status").GetString()).Distinct().ShouldBe(["Refused"]);
    }

    [Fact]
    public async Task A_report_refuses_a_grouping_sort_or_column_it_does_not_have()
    {
        using var admin = await AdminOnDemoAsync();
        (await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { groupBy = Nonsense }, HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("reports.group_invalid");
        (await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { sort = new[] { new { field = "nonsense", direction = "ASC" } } }, HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("reports.sort_invalid");
        (await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("f_nonsense", "x")) }, HttpStatusCode.BadRequest)).GetProperty("code").GetString().ShouldBe("reports.filter_invalid");
    }

    [Fact]
    public async Task Drilling_from_OTD_to_the_late_deliveries_keeps_the_period_and_the_transporter_the_user_was_looking_at()
    {
        using var admin = await AdminOnDemoAsync();
        var scope = Filters(("period", "Rolling90"), ("transporter", "Om Logistics"));

        var summary = await RunAsync(admin, "R14_OTP_OTD", new { filters = scope, pageSize = 100 });
        summary.GetProperty("drills").EnumerateArray().Select(d => d.GetProperty("targetReport").GetString()).ShouldContain("R17_DELIVERY_PERFORMANCE");

        var late = await RunAsync(admin, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("period", "Rolling90"), ("transporter", "Om Logistics"), ("onTime", "No")), pageSize = 500 });
        var rows = late.GetProperty("rows").EnumerateArray().ToList();
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.GetProperty("transporter").GetString() == "Om Logistics" && r.GetProperty("onTime").GetString() == "No");
        late.GetProperty("filtersApplied").GetProperty("transporter").GetString().ShouldBe("Om Logistics");
        late.GetProperty("period").GetProperty("kind").GetString().ShouldBe("Rolling90");

        var shipment = rows[0].GetProperty("shipment").GetString()!;
        var three60 = await RunAsync(admin, "R36_SHIPMENT_360", new { filters = Filters(("shipment", shipment)) });
        three60.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("key").GetString()).ShouldContain("timeline");
    }

    [Fact]
    public async Task Dashboards_show_current_previous_and_trend_with_the_data_freshness_visible()
    {
        using var admin = await AdminOnDemoAsync();
        var first = await JsonAsync(await admin.GetAsync("/api/v1/dashboards/executive?period=Rolling30&refresh=1"));
        first.GetProperty("cards").GetArrayLength().ShouldBe(23);
        var otd = first.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "OTD");
        otd.GetProperty("value").GetDecimal().ShouldBeGreaterThan(0);
        otd.GetProperty("numerator").GetDecimal().ShouldBeGreaterThan(0);
        otd.GetProperty("denominator").GetDecimal().ShouldBeGreaterThan(otd.GetProperty("numerator").GetDecimal());
        otd.GetProperty("previous").ValueKind.ShouldBe(JsonValueKind.Number);
        Trends.ShouldContain(otd.GetProperty("trend").GetString());
        otd.GetProperty("calculationVersion").GetString().ShouldBe("1.0");
        first.GetProperty("generatedAtUtc").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-2));
        first.GetProperty("dataSourceMode").GetString().ShouldBe("Demo");
        first.GetProperty("charts").GetArrayLength().ShouldBe(6);

        var cached = await JsonAsync(await admin.GetAsync("/api/v1/dashboards/executive?period=Rolling30"));
        cached.GetProperty("fromCache").GetBoolean().ShouldBeTrue();
        cached.GetProperty("dataFreshnessSeconds").GetInt32().ShouldBeGreaterThanOrEqualTo(0);

        var tower = await JsonAsync(await admin.GetAsync("/api/v1/dashboards/control-tower"));
        tower.GetProperty("totals").EnumerateArray().Select(t => t.GetProperty("key").GetString()).ShouldContain("lost");
        tower.GetProperty("charts")[0].GetProperty("kind").GetString().ShouldBe("map");
    }

    [Fact]
    public async Task The_kpi_service_calculates_by_code_with_parts_and_keeps_commercial_figures_from_those_who_may_not_see_cost()
    {
        using var admin = await AdminOnDemoAsync();
        var kpis = await (await admin.GetAsync("/api/v1/kpis")).ReadAsync<List<KpiDefinitionDto>>();
        kpis.Count.ShouldBeGreaterThan(25);
        kpis.Single(k => k.KpiCode == "OTD").SourceOfTruth.ShouldContain("POD");

        var outcome = await (await admin.GetAsync("/api/v1/kpis/OTD/value?period=Rolling30")).ReadAsync<KpiOutcome>();
        outcome.Current.Measurable.ShouldBeTrue();
        outcome.Current.Denominator.ShouldBeGreaterThan(0);
        outcome.Previous.ShouldNotBeNull();

        using var planner = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Planning);
        (await planner.GetAsync("/api/v1/kpis/OTD/value")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await planner.GetAsync("/api/v1/kpis/FREIGHT_SPEND/value")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await planner.GetAsync("/api/v1/kpis")).ReadAsync<List<KpiDefinitionDto>>()).ShouldNotContain(k => k.KpiCode == "FREIGHT_SPEND");
    }

    // ---- security

    [Fact]
    public async Task Access_follows_role_a_transport_user_gets_planning_but_not_cost_and_a_finance_user_gets_cost_but_not_planning()
    {
        using var admin = await AdminOnDemoAsync();
        using var transport = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Planning, ReportingPermissions.Transporters, ReportingPermissions.Delivery, ReportingPermissions.Tracking);
        using var finance = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Contracts, ReportingPermissions.Cross);
        using var nobody = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read);

        (await RunAsync(transport, "R02_LOAD_PLANNING_SUMMARY")).GetProperty("reportCode").GetString().ShouldBe("R02_LOAD_PLANNING_SUMMARY");
        await RunAsync(transport, "R35_FREIGHT_RATING_AUDIT", null, HttpStatusCode.Forbidden);
        await RunAsync(finance, "R35_FREIGHT_RATING_AUDIT");
        await RunAsync(finance, "R02_LOAD_PLANNING_SUMMARY", null, HttpStatusCode.Forbidden);
        await RunAsync(nobody, "R01_EXECUTIVE_DASHBOARD", null, HttpStatusCode.Forbidden);

        (await (await transport.GetAsync(Reports)).ReadAsync<List<ReportSummaryDto>>()).ShouldNotContain(r => r.Category == "Freight Contract Management");
        (await (await nobody.GetAsync(Reports)).ReadAsync<List<ReportSummaryDto>>()).ShouldBeEmpty();
        (await transport.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(new ReportSettings(), 0))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_transporter_sees_only_vendor_safe_reports_cannot_name_another_company_and_never_gets_rankings_or_contract_rates()
    {
        using var admin = await AdminOnDemoAsync();
        var mine = await TransporterTestData.CreateReadyAsync(admin);
        var theirs = await TransporterTestData.CreateReadyAsync(admin);
        using var vendor = await VendorAsync(admin, mine.Id);

        var visible = await (await vendor.GetAsync(Reports)).ReadAsync<List<ReportSummaryDto>>();
        visible.ShouldNotBeEmpty();
        visible.ShouldAllBe(r => r.VendorSafe);
        visible.Select(r => r.ReportCode).ShouldNotContain("R10_TRANSPORTER_RANKING");
        visible.Select(r => r.ReportCode).ShouldNotContain("R35_FREIGHT_RATING_AUDIT");

        await RunAsync(vendor, "R10_TRANSPORTER_RANKING", null, HttpStatusCode.Forbidden);
        await RunAsync(vendor, "R35_FREIGHT_RATING_AUDIT", null, HttpStatusCode.Forbidden);
        await RunAsync(vendor, "R01_EXECUTIVE_DASHBOARD", null, HttpStatusCode.Forbidden);
        (await vendor.PostAsync("/api/v1/report-subscriptions", null)).StatusCode.ShouldNotBe(HttpStatusCode.Created);

        // Another company's id in a filter is answered as not found, whatever the report.
        var asked = await RunAsync(vendor, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("transporter", theirs.Id.ToString())) }, HttpStatusCode.NotFound);
        asked.GetProperty("code").GetString().ShouldBe("reports.not_found");

        // Their own report holds nothing of anyone else, even though the demonstration data belongs to other companies.
        var own = await RunAsync(vendor, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("period", "Rolling90")), pageSize = 500 });
        own.GetProperty("totalRows").GetInt32().ShouldBe(0);
        var scorecard = await RunAsync(vendor, "R09_TRANSPORTER_SCORECARD", new { filters = Filters(("transporter", "Safexpress Carriers")) });
        scorecard.GetProperty("rows").GetArrayLength().ShouldBe(0);

        (await vendor.GetAsync("/api/v1/kpis/FREIGHT_SPEND/value")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendor.GetAsync("/api/v1/kpis/OTD/value")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await vendor.GetAsync($"{Reports}/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendor.GetAsync($"{Reports}/scopes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_limited_to_some_customers_sees_only_those_and_is_refused_reports_that_cannot_be_limited_to_customers()
    {
        using var admin = await AdminOnDemoAsync();
        var role = await admin.CreateRoleAsync(ReportingPermissions.Read, ReportingPermissions.Delivery, ReportingPermissions.Cross);
        var email = ApiExtensions.UniqueEmail("customer-service");
        var user = await admin.CreateUserAsync(email, [role.Id]);
        (await admin.PutJsonAsync($"{Reports}/scopes", new SaveDataScopeRequest(user.Id, "Customer", ["Reliance Retail", "Asian Paints"]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var limited = await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);

        var result = await RunAsync(limited, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("period", "Rolling90")), pageSize = 500 });
        var customers = result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("customer").GetString()).Distinct().Order().ToList();
        customers.ShouldNotBeEmpty();
        customers.ShouldBeSubsetOf(["Asian Paints", "Reliance Retail"]);

        // A customer filter outside the limit finds nothing: the limit is applied on the server, not in the query string.
        var other = await RunAsync(limited, "R17_DELIVERY_PERFORMANCE", new { filters = Filters(("period", "Rolling90"), ("customer", "ITC Foods")) });
        other.GetProperty("totalRows").GetInt32().ShouldBe(0);

        (await RunAsync(limited, "R37_LANE_PERFORMANCE", null, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("reports.scope_unsupported");
        var scopes = await (await admin.GetAsync($"{Reports}/scopes?userId={user.Id}")).ReadAsync<List<DataScopeDto>>();
        scopes.Single().Values.ShouldBe(["Asian Paints", "Reliance Retail"]);
    }

    [Fact]
    public async Task Finance_sees_freight_in_the_shipment_360_but_customer_service_does_not()
    {
        using var admin = await AdminOnDemoAsync();
        using var finance = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Cross, ReportingPermissions.Contracts);
        using var service = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Cross);
        var filters = Filters(("shipment", "SH-20150"));

        var rich = await RunAsync(finance, "R36_SHIPMENT_360", new { filters });
        var keys = rich.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();
        keys.ShouldBe(["order", "planning", "vehicle", "transporter", "contract", "tracking", "delivery", "pod", "discrepancies", "exceptions", "freight", "timeline"]);
        var freight = rich.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "freight").GetProperty("items");
        freight.EnumerateArray().Single(i => i.GetProperty("label").GetString() == "Final freight").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Number);

        var plain = await RunAsync(service, "R36_SHIPMENT_360", new { filters });
        plain.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("key").GetString()).ShouldNotContain("freight");
        var list = await JsonAsync(await service.PostJsonAsync($"{Reports}/R36_SHIPMENT_360/execute", new { pageSize = 500 }));
        list.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("freight").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Cross_module_analytics_return_real_comparisons_for_lanes_carriers_and_cost_against_service()
    {
        using var admin = await AdminOnDemoAsync();
        var window = Filters(("period", "Rolling90"));

        var lanes = await RunAsync(admin, "R37_LANE_PERFORMANCE", new { filters = window, pageSize = 100 });
        lanes.GetProperty("rows").GetArrayLength().ShouldBeGreaterThan(8);
        lanes.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("otd").ToString()).Distinct().Count().ShouldBeGreaterThan(3);

        var matrix = await RunAsync(admin, "R38_COST_VS_PERFORMANCE", new { filters = window });
        matrix.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("quadrant").GetString()).Distinct().Count().ShouldBeGreaterThan(2);
        matrix.GetProperty("charts")[0].GetProperty("kind").GetString().ShouldBe("scatter");
        matrix.GetProperty("charts")[0].GetProperty("lines").GetArrayLength().ShouldBe(2);

        var service = await RunAsync(admin, "R39_COST_VS_SERVICE", new { filters = window });
        service.GetProperty("sections")[0].GetProperty("items")[0].GetProperty("value").GetString().ShouldNotBeNullOrWhiteSpace();

        var audit = await RunAsync(admin, "R35_FREIGHT_RATING_AUDIT", new { filters = window, pageSize = 20 });
        audit.GetProperty("totalRows").GetInt32().ShouldBeGreaterThan(200);
        var line = audit.GetProperty("rows")[0];
        foreach (var field in AuditFields)
        {
            line.TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        var onlyOne = await RunAsync(admin, "R36_SHIPMENT_360", new { filters = Filters(("period", "Rolling90"), ("customer", "Reliance Retail")), pageSize = 500 });
        onlyOne.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("customer").GetString() == "Reliance Retail");
    }

    // ---- export and jobs

    [Fact]
    public async Task Exports_come_as_csv_excel_and_pdf_are_audited_and_need_the_export_permission()
    {
        using var admin = await AdminOnDemoAsync();
        var body = new ExportRequest(Filters(("period", "Rolling90")), null, null, "csv");
        foreach (var (format, signature) in Formats)
        {
            var made = await admin.PostJsonAsync($"{Reports}/R17_DELIVERY_PERFORMANCE/export", body with { Format = format });
            made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
            var outcome = await made.ReadAsync<ExportOutcome>();
            outcome.Immediate.ShouldBeTrue();
            outcome.Job.Status.ShouldBe("Completed");
            outcome.Job.RowCount!.Value.ShouldBeGreaterThan(300);

            var file = await admin.GetAsync($"{Reports}/jobs/{outcome.Job.Id}/download");
            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            file.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var bytes = await file.Content.ReadAsByteArrayAsync();
            Encoding.UTF8.GetString(bytes).StartsWith(signature, StringComparison.Ordinal).ShouldBeTrue(format);
            file.Content.Headers.ContentDisposition!.FileName!.ShouldContain("r17_delivery_performance");
        }

        using var viewer = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Delivery);
        var refused = await viewer.PostJsonAsync($"{Reports}/R17_DELIVERY_PERFORMANCE/export", body);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ProblemCodeAsync()).ShouldBe("reports.export_forbidden");
        (await admin.PostJsonAsync($"{Reports}/R17_DELIVERY_PERFORMANCE/export", body with { Format = "docx" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync($"{Reports}/R05_UNPLANNED/export", body with { Format = "pdf" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // an operational list offers csv and excel only

        var audit = await (await admin.GetAsync($"{Reports}/audit?report=R17_DELIVERY_PERFORMANCE&pageSize=100")).ReadAsync<PagedResult<AuditEntryDto>>();
        audit.Items.Count(a => a.Action == "Exported").ShouldBeGreaterThanOrEqualTo(3);
        audit.Items.Count(a => a.Action == "Downloaded").ShouldBeGreaterThanOrEqualTo(3);
        audit.Items.ShouldContain(a => a.Action == "ExportDenied" || audit.Items.Any(x => x.Action == "Exported"));
        audit.Items.First(a => a.Action == "Exported").UserName.ShouldNotBeNullOrWhiteSpace();
        (await viewer.GetAsync($"{Reports}/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_big_export_runs_in_the_background_the_requester_is_told_and_only_they_can_download_it()
    {
        using var admin = await AdminOnDemoAsync(syncExportRows: 100);
        var queued = await admin.PostJsonAsync($"{Reports}/R17_DELIVERY_PERFORMANCE/export", new ExportRequest(Filters(("period", "Rolling90")), null, null, "xlsx"));
        queued.StatusCode.ShouldBe(HttpStatusCode.Accepted, await queued.Content.ReadAsStringAsync());
        var outcome = await queued.ReadAsync<ExportOutcome>();
        outcome.Immediate.ShouldBeFalse();
        outcome.Job.Status.ShouldBe("Queued");

        var done = await WaitForAsync(admin, outcome.Job.Id, ReportJobStatus.Completed, ReportJobStatus.Failed);
        done.Status.ShouldBe("Completed", done.ErrorMessage);
        done.Progress.ShouldBe(100);
        done.CanDownload.ShouldBeTrue();
        done.ExpiresAt!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddHours(24));
        var mail = factory.Services.GetRequiredService<CapturingEmailSender>().Sent.Where(m => m.Subject.Contains("Your report is ready", StringComparison.Ordinal) && m.TextBody.Contains(done.JobReference, StringComparison.Ordinal));
        mail.ShouldNotBeEmpty("the requester is told the file is ready");

        var file = await admin.GetAsync($"{Reports}/jobs/{done.Id}/download");
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await file.Content.ReadAsByteArrayAsync()).Length.ShouldBe((int)done.SizeBytes!.Value);

        using var other = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Delivery, ReportingPermissions.Export);
        (await other.GetAsync($"{Reports}/jobs/{done.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"{Reports}/jobs/{done.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await other.GetAsync($"{Reports}/jobs")).ReadAsync<List<ReportJobDto>>()).ShouldBeEmpty();

        var mine = await (await admin.GetAsync($"{Reports}/jobs")).ReadAsync<List<ReportJobDto>>();
        mine.ShouldContain(j => j.Id == done.Id && j.DownloadCount == 1);
    }

    [Fact]
    public async Task A_background_export_that_fails_can_be_cancelled_or_tried_again_and_a_finished_one_cannot_be_cancelled()
    {
        using var admin = await AdminOnDemoAsync();
        var meta = await (await admin.GetAsync($"{Reports}/R05_UNPLANNED/metadata")).ReadAsync<ReportMetadataDto>();
        // The report is switched off after the export was asked for, so the worker cannot run it: the job fails, honestly.
        var queued = await (await admin.PostJsonAsync($"{Reports}/R05_UNPLANNED/export", new ExportRequest(null, null, null, "csv", Background: true))).ReadAsync<ExportOutcome>();
        (await admin.PutJsonAsync($"{Reports}/R05_UNPLANNED/definition", new UpdateReportRequest(null, null, null, "Disabled", null, await VersionOfAsync(admin, "R05_UNPLANNED")))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var failed = await WaitForAsync(admin, queued.Job.Id, ReportJobStatus.Failed, ReportJobStatus.Completed);
        failed.Status.ShouldBe("Failed");
        failed.ErrorMessage.ShouldNotBeNullOrWhiteSpace();

        (await admin.PutJsonAsync($"{Reports}/R05_UNPLANNED/definition", new UpdateReportRequest(null, null, null, "Active", null, await VersionOfAsync(admin, "R05_UNPLANNED")))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"{Reports}/jobs/{failed.Id}/retry", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var retried = await WaitForAsync(admin, failed.Id, ReportJobStatus.Completed, ReportJobStatus.Failed);
        retried.Status.ShouldBe("Completed", retried.ErrorMessage);
        (await admin.PostAsync($"{Reports}/jobs/{retried.Id}/retry", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostAsync($"{Reports}/jobs/{retried.Id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        meta.ReportCode.ShouldBe("R05_UNPLANNED");
    }

    private static async Task<long> VersionOfAsync(HttpClient admin, string code) =>
        (await (await admin.GetAsync($"{Reports}/{code}/metadata")).ReadAsync<ReportMetadataDto>()).Version;

    /// <summary>The organisation id, read from the signed-in user's own access token.</summary>
    private static Guid TenantOf(HttpClient admin)
    {
        var payload = admin.DefaultRequestHeaders.Authorization!.Parameter!.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.GetProperty("tid").GetGuid();
    }

    // ---- scheduling

    [Fact]
    public async Task Scheduled_reports_validate_their_schedule_compute_the_next_run_and_can_be_changed_paused_run_now_and_deleted()
    {
        using var admin = await AdminOnDemoAsync();
        var daily = new SaveSubscriptionRequest("R19_POD_AGEING", "POD ageing over 7 days", Filters(("minAgeDays", "7")), "Daily", new ScheduleDefinition("08:00"), "csv", "Asia/Kolkata", ["ops@example.test"]);
        var created = await admin.PostJsonAsync("/api/v1/report-subscriptions", daily);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var sub = await created.ReadAsync<SubscriptionDto>();
        sub.Active.ShouldBeTrue();
        sub.NextRunAt.ShouldNotBeNull();
        TimeZoneInfo.ConvertTime(sub.NextRunAt!.Value, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).TimeOfDay.ShouldBe(new TimeSpan(8, 0, 0));
        sub.Summary!.ShouldContain("Every day at 08:00");
        sub.Filters["minAgeDays"].ShouldBe("7");

        var weekly = await (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { Name = "Weekly scorecard", ReportCode = "R09_TRANSPORTER_SCORECARD", ScheduleType = "Weekly", Schedule = new ScheduleDefinition("09:00", "Monday"), Format = "xlsx" })).ReadAsync<SubscriptionDto>();
        TimeZoneInfo.ConvertTime(weekly.NextRunAt!.Value, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DayOfWeek.ShouldBe(DayOfWeek.Monday);
        var monthly = await (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { Name = "Spend by lane", ReportCode = "R37_LANE_PERFORMANCE", ScheduleType = "Monthly", Schedule = new ScheduleDefinition("07:00", DayOfMonth: 1), Format = "xlsx" })).ReadAsync<SubscriptionDto>();
        TimeZoneInfo.ConvertTime(monthly.NextRunAt!.Value, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).Day.ShouldBe(1);

        (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { Schedule = new ScheduleDefinition("8am") })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { TimeZone = "Mars/Olympus" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { Recipients = ["not-an-email"] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { Format = "pdf", ReportCode = "R05_UNPLANNED" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync("/api/v1/report-subscriptions", daily with { ScheduleType = "Custom", Schedule = new ScheduleDefinition(EveryMinutes: 5) })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var changed = await (await admin.PutJsonAsync($"/api/v1/report-subscriptions/{sub.Id}", daily with { Schedule = new ScheduleDefinition("18:30"), TimeZone = "UTC", Version = sub.Version })).ReadAsync<SubscriptionDto>();
        changed.TimeZone.ShouldBe("UTC");
        changed.NextRunAt!.Value.TimeOfDay.ShouldBe(new TimeSpan(18, 30, 0));
        (await admin.PutJsonAsync($"/api/v1/report-subscriptions/{sub.Id}", daily with { Version = sub.Version })).StatusCode.ShouldBe(HttpStatusCode.Conflict); // a stale copy cannot overwrite

        var paused = await (await admin.PutJsonAsync($"/api/v1/report-subscriptions/{sub.Id}", daily with { Active = false, Version = changed.Version })).ReadAsync<SubscriptionDto>();
        paused.Active.ShouldBeFalse();
        paused.NextRunAt.ShouldBeNull();

        var active = await (await admin.PutJsonAsync($"/api/v1/report-subscriptions/{sub.Id}", daily with { Version = paused.Version })).ReadAsync<SubscriptionDto>();
        var now = await admin.PostAsync($"/api/v1/report-subscriptions/{active.Id}/run-now", null);
        now.StatusCode.ShouldBe(HttpStatusCode.OK, await now.Content.ReadAsStringAsync());
        var job = await WaitForAsync(admin, (await now.ReadAsync<ReportJobDto>()).Id, ReportJobStatus.Completed, ReportJobStatus.Failed);
        job.Status.ShouldBe("Completed", job.ErrorMessage);
        job.FromSchedule.ShouldBeTrue();
        var notice = factory.Services.GetRequiredService<CapturingEmailSender>().Sent.Where(m => m.To == "ops@example.test" && m.Subject.Contains("Scheduled report", StringComparison.Ordinal)).ToList();
        notice.ShouldNotBeEmpty("listed recipients are told");
        notice[^1].TextBody.ShouldContain("/reports/R19_POD_AGEING?minAgeDays=7");
        notice[^1].TextBody.ShouldNotContain("DLV-", Case.Sensitive, "a notice carries a link, never report data");

        var mine = await (await admin.GetAsync("/api/v1/report-subscriptions")).ReadAsync<List<SubscriptionDto>>();
        mine.Count(s => s.UserId == active.UserId).ShouldBeGreaterThanOrEqualTo(3);
        (await admin.DeleteAsync($"/api/v1/report-subscriptions/{sub.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/v1/report-subscriptions/{sub.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var other = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Delivery, ReportingPermissions.Schedule);
        (await other.DeleteAsync($"/api/v1/report-subscriptions/{weekly.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await other.GetAsync("/api/v1/report-subscriptions")).ReadAsync<List<SubscriptionDto>>()).ShouldBeEmpty();
        var audit = await (await admin.GetAsync($"{Reports}/audit?action=SubscriptionCreated&pageSize=100")).ReadAsync<PagedResult<AuditEntryDto>>();
        audit.Items.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_due_subscription_is_turned_into_a_job_by_the_scheduler_and_one_whose_owner_lost_access_stops()
    {
        using var admin = await AdminOnDemoAsync();
        var role = await admin.CreateRoleAsync(ReportingPermissions.Read, ReportingPermissions.Delivery, ReportingPermissions.Export, ReportingPermissions.Schedule);
        var email = ApiExtensions.UniqueEmail("scheduler-owner");
        var owner = await admin.CreateUserAsync(email, [role.Id]);
        using var client = await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);
        var sub = await (await client.PostJsonAsync("/api/v1/report-subscriptions", new SaveSubscriptionRequest("R17_DELIVERY_PERFORMANCE", "Due now", null, "Custom", new ScheduleDefinition(EveryMinutes: 15), "csv", "UTC", null))).ReadAsync<SubscriptionDto>();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tms.Modules.Reports.Infrastructure.Persistence.ReportsDbContext>();
            scope.ServiceProvider.GetRequiredService<Tms.SharedKernel.Security.IAmbientUserContext>().RunAs(TenantOf(admin), owner.Id, "test");
            var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Subscriptions.Where(s => s.Id == sub.Id));
            row.Change(row.Name, row.ParametersJson, row.ScheduleType, row.ScheduleDefinitionJson, row.Format, row.TimeZone, row.RecipientsJson, true, row.PrincipalJson, DateTimeOffset.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();
        }

        // The scheduler is a background service with its own timer (every few seconds under test): the due subscription is picked up without any call here.
        var job = await WaitForJobAsync(client, sub.Id);
        job.Status.ShouldBe("Completed", job.ErrorMessage);

        var after = (await (await client.GetAsync("/api/v1/report-subscriptions")).ReadAsync<List<SubscriptionDto>>()).Single(s => s.Id == sub.Id);
        after.LastRunAt.ShouldNotBeNull();
        after.NextRunAt!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        // The owner loses the permission: the next due run stops the schedule instead of sending what they may no longer see.
        (await admin.PutJsonAsync($"/api/v1/roles/{role.Id}", new Tms.Modules.Platform.Application.Roles.SaveRoleRequest(role.Name, null, [ReportingPermissions.Read], role.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tms.Modules.Reports.Infrastructure.Persistence.ReportsDbContext>();
            scope.ServiceProvider.GetRequiredService<Tms.SharedKernel.Security.IAmbientUserContext>().RunAs(TenantOf(admin), owner.Id, "test");
            var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Subscriptions.Where(s => s.Id == sub.Id));
            row.Change(row.Name, row.ParametersJson, row.ScheduleType, row.ScheduleDefinitionJson, row.Format, row.TimeZone, row.RecipientsJson, true, row.PrincipalJson, DateTimeOffset.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();
        }

        SubscriptionDto stopped = after;
        for (var i = 0; i < 40 && stopped.Active; i++)
        {
            await Task.Delay(500);
            stopped = (await (await client.GetAsync("/api/v1/report-subscriptions")).ReadAsync<List<SubscriptionDto>>()).Single(s => s.Id == sub.Id);
        }

        stopped.Active.ShouldBeFalse();
    }

    private static async Task<ReportJobDto> WaitForJobAsync(HttpClient client, Guid subscriptionId)
    {
        for (var i = 0; i < 60; i++)
        {
            var jobs = await (await client.GetAsync($"{Reports}/jobs")).ReadAsync<List<ReportJobDto>>();
            if (jobs.FirstOrDefault(j => j.FromSchedule && j.Status is "Completed" or "Failed") is { } done)
            {
                return done;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Subscription {subscriptionId} produced no finished job.");
    }

    // ---- settings, definitions, summary

    [Fact]
    public async Task Settings_definitions_and_ageing_buckets_are_data_and_invalid_values_are_refused()
    {
        using var admin = await AdminOnDemoAsync();
        var current = await (await admin.GetAsync($"{Reports}/settings")).ReadAsync<SettingsDto>();

        foreach (var bad in new[]
                 {
                     current.Settings with { AgeingBuckets = [5, 3] },
                     current.Settings with { WorkingDays = [] },
                     current.Settings with { Holidays = ["not a date"] },
                     current.Settings with { DataSource = "Somewhere" },
                     current.Settings with { SyncExportRows = 5 },
                 })
        {
            (await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(bad, current.Version))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var narrow = await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(current.Settings with { AgeingBuckets = [2, 10] }, current.Version));
        narrow.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ageing = await RunAsync(admin, "R19_POD_AGEING", new { filters = Filters(("openOnly", "false")), pageSize = 500 });
        ageing.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("bucket").GetString()).Distinct().Order().ShouldBeSubsetOf(["0–2 days", "3–10 days", ">10 days"]);
        (await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(current.Settings, current.Version + 99))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var fresh = await (await admin.GetAsync($"{Reports}/settings")).ReadAsync<SettingsDto>();
        (await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(current.Settings with { AgeingBuckets = [1, 3, 7, 15, 30] }, fresh.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A report can be renamed and a column hidden without a release; a required permission must be a reports permission.
        var renamed = await admin.PutJsonAsync($"{Reports}/R23_FAILED_REFUSED/definition", new UpdateReportRequest("Failed and refused", null, null, null, [new UpdateColumnRequest("location", null, false, null)], await VersionOfAsync(admin, "R23_FAILED_REFUSED")));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var meta = await (await admin.GetAsync($"{Reports}/R23_FAILED_REFUSED/metadata")).ReadAsync<ReportMetadataDto>();
        meta.Name.ShouldBe("Failed and refused");
        meta.Columns.Single(c => c.Field == "location").Visible.ShouldBeFalse();
        var executed = await RunAsync(admin, "R23_FAILED_REFUSED");
        executed.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("field").GetString()).ShouldNotContain("location");
        (await admin.PutJsonAsync($"{Reports}/R23_FAILED_REFUSED/definition", new UpdateReportRequest(null, null, "users.manage", null, null, await VersionOfAsync(admin, "R23_FAILED_REFUSED")))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutJsonAsync($"{Reports}/R23_FAILED_REFUSED/definition", new UpdateReportRequest("Failed / refused deliveries", null, null, null, [new UpdateColumnRequest("location", null, true, null)], await VersionOfAsync(admin, "R23_FAILED_REFUSED")))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Favourites_and_dashboard_choices_are_kept_per_person()
    {
        using var admin = await AdminOnDemoAsync();
        var saved = await admin.PutJsonAsync($"{Reports}/preferences", new SavePreferenceRequest(["R09_TRANSPORTER_SCORECARD", "R19_POD_AGEING"], ["R01_EXECUTIVE_DASHBOARD"], new Dictionary<string, string> { ["period"] = "Rolling30" }, 60));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var pref = await saved.ReadAsync<PreferenceDto>();
        pref.Favourites.ShouldBe(["R09_TRANSPORTER_SCORECARD", "R19_POD_AGEING"]);
        pref.RefreshSeconds.ShouldBe(60);
        var list = await (await admin.GetAsync(Reports)).ReadAsync<List<ReportSummaryDto>>();
        list.Where(r => r.IsFavourite).Select(r => r.ReportCode).Order().ShouldBe(["R09_TRANSPORTER_SCORECARD", "R19_POD_AGEING"]);
        (await admin.PutJsonAsync($"{Reports}/preferences", new SavePreferenceRequest(["NOPE"], null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutJsonAsync($"{Reports}/preferences", new SavePreferenceRequest(null, null, null, 5))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var someoneElse = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Delivery);
        (await (await someoneElse.GetAsync($"{Reports}/preferences")).ReadAsync<PreferenceDto>()).Favourites.ShouldBeEmpty();
        await admin.PutJsonAsync($"{Reports}/preferences", new SavePreferenceRequest([], null, null, 0));
    }

    [Fact]
    public async Task The_kept_daily_kpi_values_reproduce_the_live_trend_exactly_and_are_rebuilt_on_request()
    {
        using var admin = await AdminOnDemoAsync();
        var live = await JsonAsync(await admin.GetAsync("/api/v1/dashboards/executive?refresh=1&period=Monthly"));
        var rebuilt = await admin.PostAsync($"{Reports}/summary/rebuild?days=200", null);
        rebuilt.StatusCode.ShouldBe(HttpStatusCode.OK, await rebuilt.Content.ReadAsStringAsync());
        (await rebuilt.ReadAsync<int>()).ShouldBeGreaterThan(200 * 20);

        var kept = await JsonAsync(await admin.GetAsync("/api/v1/dashboards/executive?refresh=1&period=Monthly"));
        decimal?[] Series(JsonElement dashboard, string chart, string key) => dashboard.GetProperty("charts").EnumerateArray().Single(c => c.GetProperty("id").GetString() == chart).GetProperty("data").EnumerateArray()
            .Select(r => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (decimal?)v.GetDecimal() : null).ToArray();
        Series(kept, "freight-trend", "FREIGHT_SPEND").ShouldBe(Series(live, "freight-trend", "FREIGHT_SPEND"), "reading the kept values must give the figures the transactions give");
        Series(kept, "service-trend", "OTD").ShouldBe(Series(live, "service-trend", "OTD"));

        using var planner = await factory.UserWithPermissionsAsync(admin, ReportingPermissions.Read, ReportingPermissions.Planning);
        (await planner.PostAsync($"{Reports}/summary/rebuild", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reports_at_the_expected_volume_stay_responsive_and_report_how_long_they_took()
    {
        using var admin = await AdminOnDemoAsync();
        foreach (var code in new[] { "R01_EXECUTIVE_DASHBOARD", "R10_TRANSPORTER_RANKING", "R17_DELIVERY_PERFORMANCE", "R37_LANE_PERFORMANCE", "R39_COST_VS_SERVICE" })
        {
            var result = await RunAsync(admin, code, new { filters = Filters(("period", "Rolling90"), ("grain", "week")), pageSize = 50, refresh = true });
            result.GetProperty("durationMs").GetInt32().ShouldBeLessThan(5_000, code);
        }
    }

    [Fact]
    public async Task Errors_use_the_standard_report_error_and_never_show_internals()
    {
        using var admin = await AdminOnDemoAsync();
        var missing = await admin.PostJsonAsync($"{Reports}/NOPE/execute", new { });
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await missing.Content.ReadAsStringAsync();
        body.ShouldNotContain("at Tms.", Case.Sensitive);
        (await JsonAsync(missing)).GetProperty("code").GetString().ShouldBe("reports.not_found");
        (await factory.CreateClient().PostAsync($"{Reports}/R17_DELIVERY_PERFORMANCE/execute", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_report_also_runs_against_the_real_modules_data_and_says_where_its_data_came_from()
    {
        using var admin = await factory.AdminAsync();
        var current = await (await admin.GetAsync($"{Reports}/settings")).ReadAsync<SettingsDto>();
        (await admin.PutJsonAsync($"{Reports}/settings", new SaveSettingsRequest(current.Settings with { DataSource = "Live" }, current.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await (await admin.GetAsync($"{Reports}/data-source")).ReadAsync<DataSourceStatusDto>();
        status.Mode.ShouldBe("Live");
        status.Providers.Values.ShouldAllBe(v => v == "Live");

        var list = await (await admin.GetAsync(Reports)).ReadAsync<List<ReportSummaryDto>>();
        foreach (var report in list)
        {
            var result = await RunAsync(admin, report.ReportCode, new { filters = Filters(("period", "Rolling90")), pageSize = 20 });
            result.GetProperty("dataSourceMode").GetString().ShouldBe("Live", report.ReportCode);
        }
    }
}
