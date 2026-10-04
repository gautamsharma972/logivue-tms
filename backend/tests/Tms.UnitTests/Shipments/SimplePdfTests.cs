using System.Text;
using Tms.Modules.Shipments.Infrastructure.Export;

namespace Tms.UnitTests.Shipments;

public class SimplePdfTests
{
    private static string Text(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    [Fact]
    public void A_report_is_a_well_formed_pdf_with_its_title_and_cells()
    {
        var pdf = new SimplePdf("Transport plan PLN-1", "Planning date 04 Oct 2026");
        pdf.Heading("Vehicles");
        pdf.Table(["Vehicle", "Cost INR"], [["MH12AB1234", "40000"], ["Tata (14 ft) \\ spare", "1500.50"]], [false, true]);

        var text = Text(pdf.ToBytes(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.FromMinutes(330))));

        text.ShouldStartWith("%PDF-1.4");
        text.TrimEnd().ShouldEndWith("%%EOF");
        text.ShouldContain("(Transport plan PLN-1) Tj");
        text.ShouldContain("(MH12AB1234) Tj");
        text.ShouldContain("(Tata \\(14 ft\\) \\\\ spare) Tj"); // parentheses and backslashes cannot end the string early
        text.ShouldContain("(Page 1 of 1) Tj");
        text.ShouldContain("/Count 1");
    }

    [Fact]
    public void A_long_table_continues_on_new_pages_with_its_header_repeated()
    {
        var pdf = new SimplePdf("Long", "x");
        pdf.Table(["Order", "Place"], Enumerable.Range(1, 150).Select(i => (IReadOnlyList<string>)[$"ORD-{i:D5}", "Somewhere, Maharashtra"]).ToList());

        var text = Text(pdf.ToBytes(DateTimeOffset.UtcNow));

        var pages = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"/Count (\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        pages.ShouldBeGreaterThan(3);
        text.ShouldContain($"(Page {pages} of {pages}) Tj");
        text.Split("(Order) Tj").Length.ShouldBe(pages + 1); // the header once per page
    }

    [Fact]
    public void Characters_outside_basic_latin_are_transliterated_not_dropped_into_garbage()
    {
        SimplePdf.Clean("₹1,200 → Pune · done – ok").ShouldBe("Rs 1,200 -> Pune | done - ok");
        SimplePdf.Clean("दिल्ली").ShouldBe("??????");
    }

    [Fact]
    public void A_word_wider_than_its_cell_is_broken_instead_of_overflowing()
    {
        var pdf = new SimplePdf("Wide", "x");
        pdf.Table(["A", "B"], [[new string('W', 300), "ok"]]);

        var text = Text(pdf.ToBytes(DateTimeOffset.UtcNow));

        text.ShouldNotContain(new string('W', 300));
        text.ShouldContain("(ok) Tj");
    }
}
