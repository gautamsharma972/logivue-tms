using System.Text;
using MiniExcelLibs;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Shipments;

public class PlanExportTests
{
    private static RunDto Run(string transporter = "Shree Roadlines", string orderNumber = "ORD-00001")
    {
        var vehicle = new PlannedVehicle(
            Guid.NewGuid(), "Pune", "Maharashtra", FreightMode.Ftl, Guid.NewGuid(), "14ft", 4000, 18m, null, "CN-00001", null, transporter, 12_000m, [], 3_000m, 10m, 0.75m, 0.55m,
            [new PlannedOrder(Guid.NewGuid(), orderNumber, 1, "Surat", "Gujarat", 3_000m, 10m)], [], "Cheapest, fits", false, null, DistanceKm: 300,
            Stops: [new PlannedStop(0, "Pickup", "Pune, Maharashtra", 18.5, 73.8, null, new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.FromMinutes(330)))]);
        var unplanned = new UnplannedOrder(Guid.NewGuid(), "ORD-00002", UnplannedCodes.NoRate, "No rate, for this lane", ["Add a rate"]);
        var plan = PlanSnapshot.Build(SolverStatus.Feasible, null, [vehicle], [unplanned]);
        return new RunDto(Guid.NewGuid(), Guid.NewGuid(), "PLN-20260701-001", 2, true, new DateOnly(2026, 7, 1), PlanStatus.PartiallyPlanned, new PlanOptions(), [], null, plan, DateTimeOffset.UtcNow, null, null, null, 1);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("Plain text", "Plain text")]
    [InlineData("", "")]
    public void Text_a_spreadsheet_could_read_as_a_formula_is_neutralised(string input, string expected) => PlanExport.Safe(input).ShouldBe(expected);

    [Fact]
    public async Task The_csv_has_one_row_per_order_including_unplanned_ones_and_quotes_commas()
    {
        var file = await PlanExport.RunAsync(Run(), "csv");

        file.ContentType.ShouldStartWith("text/csv");
        file.FileName.ShouldBe("PLN-20260701-001-v2.csv");
        var text = Encoding.UTF8.GetString(file.Content).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(3); // header + planned order + unplanned order
        lines[0].ShouldStartWith("Plan,Version,Status,Vehicle");
        lines[1].ShouldContain("ORD-00001");
        lines[1].ShouldContain("Planned");
        lines[2].ShouldContain("Unplanned");
        lines[2].ShouldContain("\"NO_VALID_RATE: No rate, for this lane\""); // the comma is inside quotes
    }

    [Fact]
    public async Task A_name_that_looks_like_a_formula_cannot_run_in_the_csv()
    {
        var file = await PlanExport.RunAsync(Run(transporter: "=HYPERLINK(\"http://evil\",\"x\")"), "csv");

        var text = Encoding.UTF8.GetString(file.Content);
        text.ShouldContain("\"'=HYPERLINK(");
        text.ShouldNotContain(",=HYPERLINK");
    }

    [Fact]
    public async Task The_excel_file_is_a_workbook_with_the_expected_sheets_and_rows()
    {
        var file = await PlanExport.RunAsync(Run(), "xlsx");

        file.ContentType.ShouldContain("spreadsheetml");
        file.Content[..2].ShouldBe([(byte)'P', (byte)'K']); // an xlsx is a zip
        using var stream = new MemoryStream(file.Content);
        MiniExcel.GetSheetNames(stream).ShouldBe(["Summary", "Vehicles", "Orders", "Stops", "Unplanned"]);
        stream.Position = 0;
        var orders = MiniExcel.Query(stream, sheetName: "Orders", useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();
        orders.Count.ShouldBe(2);
        orders[0]["Order"].ToString().ShouldBe("ORD-00001");
    }

    [Fact]
    public async Task The_dashboard_exports_in_both_formats()
    {
        var kpis = PlanningKpiCalculator.Calculate([new KpiPlan(new DateOnly(2026, 7, 1), PlanStatus.Completed, Run().Plan)]);
        var dto = new DashboardDto(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), kpis);

        var csv = await PlanExport.DashboardAsync(dto, "csv");
        var xlsx = await PlanExport.DashboardAsync(dto, "xlsx");

        Encoding.UTF8.GetString(csv.Content).ShouldContain("Total freight INR");
        csv.FileName.ShouldBe("planning-kpis-20260701-20260731.csv");
        using var stream = new MemoryStream(xlsx.Content);
        MiniExcel.GetSheetNames(stream).ShouldBe(["KPIs", "Daily", "Unplanned reasons", "Transporters", "Vehicle types"]);
    }

    [Fact]
    public void Only_csv_xlsx_and_pdf_are_accepted()
    {
        PlanExport.IsKnown("CSV").ShouldBeTrue();
        PlanExport.IsKnown("xlsx").ShouldBeTrue();
        PlanExport.IsKnown("PDF").ShouldBeTrue();
        PlanExport.IsKnown("docx").ShouldBeFalse();
        PlanExport.IsKnown(null).ShouldBeFalse();
    }
}
