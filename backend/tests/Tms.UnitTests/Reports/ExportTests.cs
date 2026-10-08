using System.Text;
using MiniExcelLibs;
using Tms.Modules.Reports.Application;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Export;

namespace Tms.UnitTests.Reports;

public class ExportTests
{
    private static ReportResult Result(params IReadOnlyDictionary<string, object?>[] rows) => new(
        "RX", "Test report ₹", "Operational", ReportTestKit.Now, 0, 5, rows.Length, 1, 50,
        [new ColumnDto("name", "Name", "Text", null, 1, true, false, true), new ColumnDto("amount", "Amount", "Currency", null, 2, true, false, true), new ColumnDto("when", "When", "Date", null, 3, true, false, true),
            new ColumnDto("other", "Name", "Text", null, 4, true, false, true)],
        rows, new Dictionary<string, object?>(), [], [], [], [], ["A note"], new Dictionary<string, string> { ["transporter"] = "Alpha" }, [], [],
        new PeriodDto(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null, null, null, "Custom"), "1.0", "Demo", "OnDemand", false);

    private static Dictionary<string, object?> Row(string name, decimal amount) => new Dictionary<string, object?> { ["name"] = name, ["amount"] = amount, ["when"] = new DateOnly(2026, 10, 7), ["other"] = "x" };

    [Fact]
    public void CSV_has_a_header_escapes_quotes_and_commas_neutralises_formulas_and_names_duplicate_columns_apart()
    {
        var file = ReportExporter.Build(Result(Row("Smith, \"Jr\"", 1234.5m), Row("=HYPERLINK(\"evil\")", 5m)), "csv", ReportTestKit.Now);

        file.ContentType.ShouldStartWith("text/csv");
        file.Content.Take(3).ToArray().ShouldBe(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(file.Content).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Name (name),Amount,When,Name (other)");
        lines[1].ShouldBe("\"Smith, \"\"Jr\"\"\",1234.5,2026-10-07,x");
        lines[2].ShouldStartWith("\"'=HYPERLINK", Case.Sensitive, "a cell that looks like a formula is written as text");
        file.FileName.ShouldMatch(@"^rx-\d{8}-\d{4}\.csv$");
    }

    [Fact]
    public void Excel_holds_the_rows_and_a_summary_sheet_with_the_filters_and_version()
    {
        var file = ReportExporter.Build(Result(Row("A", 1m), Row("B", 2m)), "xlsx", ReportTestKit.Now);

        file.Content.Take(2).ToArray().ShouldBe("PK"u8.ToArray());
        using var stream = new MemoryStream(file.Content);
        MiniExcel.GetSheetNames(stream).ShouldBe(["Report", "Summary"]);
        stream.Position = 0;
        var rows = MiniExcel.Query(stream, sheetName: "Report", useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();
        rows.Count.ShouldBe(2);
        stream.Position = 0;
        var summary = MiniExcel.Query(stream, sheetName: "Summary", useHeaderRow: true).Cast<IDictionary<string, object?>>().Select(r => $"{r["Section"]}|{r["Item"]}|{r["Value"]}").ToList();
        summary.ShouldContain("Filters|transporter|Alpha");
        summary.ShouldContain("Report|Calculation version|1.0");
    }

    [Fact]
    public void PDF_is_a_valid_document_with_only_latin_text_and_page_numbers()
    {
        var rows = Enumerable.Range(0, 400).Select(i => Row($"Row {i} → ₹", i)).ToArray();
        var file = ReportExporter.Build(Result(rows), "pdf", ReportTestKit.Now);

        var text = Encoding.Latin1.GetString(file.Content);
        text.ShouldStartWith("%PDF-1.4");
        text.TrimEnd().ShouldEndWith("%%EOF");
        text.ShouldContain("Page 1 of ");
        text.ShouldContain("Rs ");
        text.ShouldNotContain("₹");
        file.Content.All(b => b < 128 || b >= 160).ShouldBeTrue();
        var pages = text.Split("/Type /Page ", StringSplitOptions.None).Length - 1;
        pages.ShouldBeGreaterThan(1, "400 rows continue over several pages with the header repeated");
    }

    [Fact]
    public void A_large_export_is_cut_for_pdf_only_and_says_so()
    {
        var rows = Enumerable.Range(0, ReportExporter.PdfRowLimit + 50).Select(i => Row("R" + i, i)).ToArray();
        var pdf = Encoding.Latin1.GetString(ReportExporter.Build(Result(rows), "pdf", ReportTestKit.Now).Content);
        pdf.ShouldContain("first 3,000 of");
        ReportExporter.Build(Result(rows), "csv", ReportTestKit.Now).Content.Length.ShouldBeGreaterThan(rows.Length * 10);
    }

    [Fact]
    public void Latin_transliterates_what_pdf_cannot_hold()
    {
        ReportPdf.Latin("₹ 5 → 6 – ok “q” …").ShouldBe("Rs  5 -> 6 - ok \"q\" ...");
        ReportPdf.Latin("日本").ShouldBe("??");
    }

    [Fact]
    public void Only_csv_xlsx_and_pdf_are_known_formats()
    {
        ReportExporter.IsKnown("xlsx").ShouldBeTrue();
        ReportExporter.IsKnown("docx").ShouldBeFalse();
    }
}
