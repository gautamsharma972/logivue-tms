using System.Globalization;
using System.Net;
using System.Text;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Import;
using Tms.Modules.Contracts.Domain;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;
using static Tms.IntegrationTests.Infrastructure.FreightApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FreightBulkApiTests(TmsApiFactory factory)
{
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string csv, ImportMode mode = ImportMode.Append, string name = "rates.csv")
    {
        using var form = new MultipartFormDataContent { { new StringContent(mode.ToString()), "mode" } };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", name);
        return await client.PostAsync("/api/v1/freight-rates/import", form);
    }

    private static string Sheet(string contract, string state, string vehicle, params string[] rows) =>
        string.Join("\n", ["Contract Number,Service Type,Origin,Destination,Vehicle Type,Weight From,Weight To,Distance From,Distance To,Rate,Rate Type,Priority,Effective From,Effective To", .. rows.Select(r => r.Replace("{C}", contract, StringComparison.Ordinal).Replace("{S}", state, StringComparison.Ordinal).Replace("{V}", vehicle, StringComparison.Ordinal))]);

    [Fact]
    public async Task A_sheet_is_checked_row_by_row_corrected_and_applied_into_a_draft_that_still_needs_approval()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var draft = await CreateAsync(admin, NewContract(transporter.Id) with { Extras = new ContractExtras(Services: [ContractType.Ptl]) });
        var number = draft.Summary.Number;
        var csv = Sheet(number, state, truck.Code,
            "{C},FTL,\"Mumbai, {S}\",\"Pune, {S}\",{V},10000,15000,150,200,38000,FIXED,100,2026-04-01,2027-03-31",   // row 2: fine
            "{C},FTL,\"Mumbai, {S}\",\"Surat, {S}\",{V},1000,500,,,40000,FIXED,100,,",                                  // row 3: weight range backwards
            "{C},FTL,\"Mumbai, {S}\",\"Pune, {S}\",{V},12000,18000,150,200,39000,FIXED,100,2026-04-01,2027-03-31",   // row 4: overlaps row 2
            "{C},FTL,\"Mumbai, {S}\",\"Nashik, {S}\",NO SUCH TRUCK,,,,,35000,FIXED,100,,",                           // row 5: unknown vehicle
            "{C},PTL,{S},{S},,0,500,,,10,PER_KG,100,,");                                                              // row 6: fine

        var uploaded = await UploadAsync(admin, csv);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        var batch = await uploaded.ReadAsync<ImportBatchDto>();

        batch.RowCount.ShouldBe(5);
        batch.ContractNumber.ShouldBe(number);
        var rows = batch.Rows!.ToDictionary(r => r.RowNumber);
        rows[2].Status.ShouldBeOneOf("Valid", "Warning"); // a warning at most: its dates start before the contract does
        rows[3].Issues.ShouldContain(i => i.Code == "RATE_TERMS_INVALID" && i.Message.Contains("weight band"));
        rows[4].Issues.ShouldContain(i => i.Code == "RATE_CONFLICT");
        rows[5].Issues.ShouldContain(i => i.Code == "ROW_VEHICLE_UNKNOWN");
        rows[6].Status.ShouldBeOneOf("Valid", "Warning");
        batch.ErrorRows.ShouldBe(3);

        var refused = await admin.PostJsonAsync($"/api/v1/freight-rates/import/{batch.Id}/apply", new ApplyImportRequest());
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("Row 3");

        // correct the two sheet mistakes; the genuine clash with row 2 stays and is skipped
        var fixedWeight = new Dictionary<string, string?>(rows[3].Values) { ["weightfrom"] = "2000", ["weightto"] = "9000" };
        var fixedVehicle = new Dictionary<string, string?>(rows[5].Values) { ["vehicletype"] = truck.Code };
        (await admin.PostJsonAsync($"/api/v1/freight-rates/import/{batch.Id}/rows/3", new CorrectImportRowRequest(fixedWeight))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = await (await admin.PostJsonAsync($"/api/v1/freight-rates/import/{batch.Id}/rows/5", new CorrectImportRowRequest(fixedVehicle))).ReadAsync<ImportBatchDto>();
        corrected.ErrorRows.ShouldBe(1);
        corrected.Rows!.Single(r => r.Status == "Error").RowNumber.ShouldBe(4);

        var applied = await admin.PostJsonAsync($"/api/v1/freight-rates/import/{batch.Id}/apply", new ApplyImportRequest(SkipInvalidRows: true));
        applied.StatusCode.ShouldBe(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var result = await applied.ReadAsync<ImportApplyResult>();
        result.Imported.ShouldBe(4);
        result.Skipped.ShouldBe(1);
        result.Contract.Summary.Status.ShouldBe(ContractStatus.Draft, "imported rates are not live: they need approval like any other change");
        result.Contract.Summary.Id.ShouldBe(draft.Summary.Id);
        (await admin.GetAsync($"/api/v1/freight-rates/import/{batch.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostJsonAsync($"/api/v1/freight-rates/import/{batch.Id}/apply", new ApplyImportRequest())).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var rates = await (await admin.GetAsync($"/api/v1/contracts/{draft.Summary.Id}/rates")).ReadAsync<List<RateCardDto>>();
        rates.Count.ShouldBe(4);
        rates.Any(r => r.Pricing is FlatTripPricing { AmountPerTrip: 38_000m } && r.Extras!.MinWeightKg == 10_000m && r.MinDistanceKm == 150m).ShouldBeTrue();
        rates.Any(r => r.Pricing is SlabRatePricing { Unit: RateUnit.Kg } && r.Extras!.MaxWeightKg == 500m).ShouldBeTrue();
    }

    [Fact]
    public async Task A_sheet_for_an_approved_contract_goes_into_a_new_draft_revision_and_leaves_the_live_contract_alone()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var active = await BuildActiveAsync(admin, transporter.Id, rates: [Flat(City(state, "Mumbai"), City(state, "Pune"), 38_000m, truck.Id)]);
        var csv = Sheet(active.Summary.Number, state, truck.Code, "{C},FTL,\"Mumbai, {S}\",\"Surat, {S}\",{V},,,,,52000,FIXED,100,,");

        var batch = await (await UploadAsync(admin, csv)).ReadAsync<ImportBatchDto>();
        batch.ErrorRows.ShouldBe(0, string.Join("; ", batch.Rows!.SelectMany(r => r.Issues.Select(i => i.Message))));
        var result = await (await admin.PostJsonAsync($"/api/v1/freight-rates/import/{batch.Id}/apply", new ApplyImportRequest())).ReadAsync<ImportApplyResult>();

        result.Contract.Summary.Id.ShouldNotBe(active.Summary.Id);
        result.Contract.Summary.Status.ShouldBe(ContractStatus.Draft);
        result.Contract.Summary.Revision.ShouldBe(2);
        result.Contract.Summary.RateCount.ShouldBe(2, "the live rate plus the imported one");
        (await GetAsync(admin, active.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.Active);
        (await GetAsync(admin, active.Summary.Id)).Summary.RateCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_sheet_with_the_wrong_contract_or_transporter_is_refused_row_by_row_and_an_unreadable_file_says_so()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var draft = await CreateAsync(admin, NewContract(transporter.Id));

        var unknown = await (await UploadAsync(admin, Sheet("CN-NOPE", state, truck.Code, "{C},FTL,\"A, {S}\",\"B, {S}\",{V},,,,,1000,FIXED,100,,"))).ReadAsync<ImportBatchDto>();
        unknown.Rows!.Single().Issues.ShouldContain(i => i.Code == "ROW_CONTRACT_UNKNOWN");
        unknown.ContractId.ShouldBeNull();
        (await admin.PostJsonAsync($"/api/v1/freight-rates/import/{unknown.Id}/apply", new ApplyImportRequest(SkipInvalidRows: true))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var mixed = await (await UploadAsync(admin, Sheet(draft.Summary.Number, state, truck.Code, "{C},FTL,\"A, {S}\",\"B, {S}\",{V},,,,,1000,FIXED,100,,", "CN-99999,FTL,\"C, {S}\",\"D, {S}\",{V},,,,,1000,FIXED,100,,"))).ReadAsync<ImportBatchDto>();
        mixed.Rows!.Single(r => r.RowNumber == 3).Issues.ShouldContain(i => i.Code == "ROW_CONTRACT_MIXED");

        (await UploadAsync(admin, string.Empty)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await UploadAsync(admin, "just some words, not a sheet")).StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_template_downloads_and_an_export_can_be_read_back_as_a_sheet()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var active = await BuildActiveAsync(admin, transporter.Id, rates: [Flat(City(state, "Mumbai"), City(state, "Pune"), 38_000m, truck.Id)]);

        var template = await admin.GetAsync("/api/v1/freight-rates/import/template?format=csv");
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await template.Content.ReadAsStringAsync()).ShouldContain("Contract Number,Contract Version,Transporter,Service Type");

        var xlsx = await admin.GetAsync("/api/v1/freight-rates/import/template");
        xlsx.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var exported = await admin.GetAsync($"/api/v1/freight-rates/export?contractId={active.Summary.Id}&format=csv");
        exported.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csv = await exported.Content.ReadAsStringAsync();
        csv.ShouldContain(active.Summary.Number);
        csv.ShouldContain("38000");

        // what was exported can be uploaded again as a revision of that contract
        var again = await (await UploadAsync(admin, csv, ImportMode.Replace)).ReadAsync<ImportBatchDto>();
        again.ContractNumber.ShouldBe(active.Summary.Number);
        again.Rows!.Single().Status.ShouldBeOneOf("Valid", "Warning");
    }

    [Fact]
    public async Task The_validate_api_reports_overlaps_with_the_row_and_field_and_clean_sets_are_valid()
    {
        using var admin = await factory.AdminAsync();
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        RateInputDto Band(decimal from, decimal to, string code) => Rate(City(state, "A"), City(state, "B"), new FlatTripPricing(30_000m), truck.Id, new RateExtras(Code: code, MinWeightKg: from, MaxWeightKg: to));

        var clash = await ValidateAsync(admin, new ValidateRatesRequest(null, [Band(500, 1_000, "R1"), Band(800, 1_500, "R2")]));
        var clean = await ValidateAsync(admin, new ValidateRatesRequest(null, [Band(0, 500, "R1"), Band(500, 1_000, "R2")]));

        clash.Outcome.ShouldBe("Invalid");
        clash.Issues.ShouldContain(i => i.Code == "RATE_CONFLICT" && i.Row == 2 && i.Severity == "Error");
        clean.Outcome.ShouldBe("Valid");
        (await ValidateAsync(admin, new ValidateRatesRequest(null, [Rate(City(state, "A"), City(state, "B"), new FlatTripPricing(30_000m), null)]))).Issues.ShouldContain(i => i.Code == "RATE_VEHICLE_MISSING");
    }

    [Fact]
    public async Task The_dashboard_expiry_coverage_validation_usage_and_renewal_impact_come_from_real_data()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var ending = await BuildActiveAsync(admin, transporter.Id, NewContract(transporter.Id, from: Today.AddDays(-340), to: Today.AddDays(20)), rates: [Flat(City(state, "A"), City(state, "B"), 40_000m, truck.Id)]);
        var shipment = $"SH-{Guid.NewGuid():N}"[..12];
        (await admin.PostJsonAsync("/api/v1/freight-rating/calculate", Rating(state, "A", state, "B", vehicle: truck.Id, km: 100m, transporter: transporter.Id, shipment: shipment, commit: true))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var summary = await (await admin.GetAsync("/api/v1/freight-contract-dashboard/summary")).ReadAsync<ContractDashboardDto>();
        summary.Active.ShouldBeGreaterThan(0);
        summary.ActiveRates.ShouldBeGreaterThan(0);
        summary.ExpiringSoon.ShouldBeGreaterThan(0);
        summary.TotalContracts.ShouldBeGreaterThan(0);

        var expiry = await (await admin.GetAsync("/api/v1/freight-contract-dashboard/expiry")).ReadAsync<ExpiryDto>();
        expiry.Bands.ShouldBe([7, 15, 30, 60, 90]);
        expiry.Items.ShouldContain(i => i.Kind == "Contract" && i.ContractNumber == ending.Summary.Number && i.Band == "30 days" && i.DaysLeft == 20);

        var usage = await (await admin.GetAsync("/api/v1/freight-contract-dashboard/rate-usage")).ReadAsync<List<RateUsageDto>>();
        usage.ShouldContain(u => u.ContractNumber == ending.Summary.Number && u.Shipments == 1 && u.TotalFreight == 40_000m);

        // the renewal costs more: the kept rating is rated again under it
        var renewal = await (await admin.PostJsonAsync($"/api/v1/contracts/{ending.Summary.Id}/renew", new RenewRequest(null, null, 10m))).ReadAsync<ContractDto>();
        var impact = await (await admin.GetAsync($"/api/v1/contracts/{renewal.Summary.Id}/renewal-impact")).ReadAsync<ImpactDto>();
        impact.ShipmentsConsidered.ShouldBe(1);
        impact.CurrentSpend.ShouldBe(40_000m);
        impact.ProposedSpend.ShouldBe(44_000m);
        impact.Variance.ShouldBe(4_000m);
        impact.VariancePercent.ShouldBe(10m);
        impact.AnnualisedVariance.ShouldBe(4_000m, "one month of history at 12 months: 4,000 x 12 / 12");
        impact.Rows.ShouldContain(r => r.ChangePercent == 10m);
        (await admin.GetAsync($"/api/v1/contracts/{ending.Summary.Id}/renewal-impact")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var validation = await (await admin.GetAsync("/api/v1/freight-contract-dashboard/validation")).ReadAsync<ValidationOverviewDto>();
        validation.ContractsChecked.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Reports_download_as_csv_or_excel_and_an_unknown_report_is_not_found()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var region = UniqueRegion();
        await AddDieselAsync(admin, region, 99m);
        await BuildActiveAsync(admin, transporter.Id, dph: [Dph(region)], rates: [Flat(City(state, "A"), City(state, "B"), 40_000m, truck.Id)]);

        var list = await (await admin.GetAsync("/api/v1/freight-contract-reports")).ReadAsync<ReportsListDto>();
        list.Reports.ShouldContain("rating-history");
        foreach (var report in list.Reports)
        {
            var response = await admin.GetAsync($"/api/v1/freight-contract-reports/{report}?format=csv");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, report);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv", report);
        }

        var dph = await (await admin.GetAsync("/api/v1/freight-contract-reports/dph?format=csv")).Content.ReadAsStringAsync();
        dph.ShouldContain(region.ToUpperInvariant());
        (await admin.GetAsync("/api/v1/freight-contract-reports/rates?format=xlsx")).Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        (await admin.GetAsync("/api/v1/freight-contract-reports/nonsense")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/v1/freight-contract-reports/rates?from=" + Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_dph_calculator_records_the_period_price_and_the_index_lists_what_was_entered()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var region = UniqueRegion();
        await AddDieselAsync(admin, region, 99m);
        var contract = await BuildActiveAsync(admin, transporter.Id, dph: [Dph(region)], rates: [Flat(City(state, "A"), City(state, "B"), 38_000m, truck.Id)]);

        var rules = await (await admin.GetAsync($"/api/v1/dph/rules?contractId={contract.Summary.Id}")).ReadAsync<List<DphOverviewDto>>();
        var rule = rules.ShouldHaveSingleItem();
        rule.CurrentPrice.ShouldBe(99m);
        rule.VariationPercent.ShouldBe(10m);
        rule.AdjustmentPercent.ShouldBe(3m);
        rule.RevisionDue.ShouldBeTrue();

        var preview = await (await admin.PostJsonAsync($"/api/v1/dph/rules/{rule.Rule.Id}/calculate", new DphCalculateRequest(null, 38_000m, null))).ReadAsync<DphCalculationDto>();
        preview.Amount.ShouldBe(1_140m);
        preview.Recorded.ShouldBeFalse();
        var recorded = await (await admin.PostJsonAsync($"/api/v1/dph/rules/{rule.Rule.Id}/calculate", new DphCalculateRequest(null, 38_000m, null, Record: true))).ReadAsync<DphCalculationDto>();
        recorded.Recorded.ShouldBeTrue();
        (await (await admin.GetAsync($"/api/v1/dph/rules?contractId={contract.Summary.Id}")).ReadAsync<List<DphOverviewDto>>()).Single().RevisionDue.ShouldBeFalse();

        var index = await (await admin.GetAsync($"/api/v1/dph/price-index?region={region}")).ReadAsync<List<PriceIndexDto>>();
        index.ShouldHaveSingleItem().Price.ShouldBe(99m);
        (await admin.PostJsonAsync("/api/v1/dph/price-index", new AddPriceIndexRequest(region, Today.AddDays(-5), 100m, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
