using System.Globalization;
using System.Text;
using MiniExcelLibs;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Export;

namespace Tms.Modules.Reports.Application;

internal sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>Writes a report's result as CSV, Excel or PDF. The file holds exactly what the person saw, no more: the result was already limited to their access.</summary>
internal static class ReportExporter
{
    public const string Csv = "text/csv; charset=utf-8";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Pdf = "application/pdf";
    public const int PdfRowLimit = 3_000;

    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static bool IsKnown(string format) => format.ToLowerInvariant() is "csv" or "xlsx" or "pdf";

    public static ExportFile Build(ReportResult r, string format, DateTimeOffset generatedAt)
    {
        var stamp = generatedAt.ToOffset(Ist).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var name = r.ReportCode.ToLowerInvariant();
        return format.ToLowerInvariant() switch
        {
            "csv" => new ExportFile(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(BuildCsv(r))).ToArray(), Csv, $"{name}-{stamp}.csv"),
            "xlsx" => new ExportFile(BuildXlsx(r, generatedAt), Xlsx, $"{name}-{stamp}.xlsx"),
            _ => new ExportFile(BuildPdf(r, generatedAt), Pdf, $"{name}-{stamp}.pdf"),
        };
    }

    // ---- values

    public static object? Raw(object? value, string dataType)
    {
        return value switch
        {
            null => null,
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset t => t.ToOffset(Ist).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTime t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            string s => dataType is nameof(FieldType.Text) or nameof(FieldType.Status) ? SafeText(s) : s,
            _ => ReportShaper.ToDecimal(value) is { } n && value is not string ? n : Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Text that starts like a formula is written as text so a sheet cannot run anything when opened.</summary>
    public static string SafeText(string s) => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? "'" + s : s;

    public static string Show(object? value, string dataType)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is DateOnly d)
        {
            return d.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset t)
        {
            return t.ToOffset(Ist).ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        if (value is bool b)
        {
            return b ? "Yes" : "No";
        }

        if (value is string s)
        {
            return s;
        }

        var n = ReportShaper.ToDecimal(value);
        if (n is null)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return dataType switch
        {
            nameof(FieldType.Currency) => "Rs " + n.Value.ToString("N0", India),
            nameof(FieldType.Percent) => n.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%",
            nameof(FieldType.Whole) => n.Value.ToString("N0", India),
            _ => n.Value.ToString("#,##0.##", India),
        };
    }

    public static string ShowUnit(object? value, string? unit) => value is null ? "Not measurable" : unit?.ToLowerInvariant() switch
    {
        "currency" => Show(value, nameof(FieldType.Currency)),
        "percent" => Show(value, nameof(FieldType.Percent)),
        "minutes" => Show(value, nameof(FieldType.Number)) + " min",
        "count" => Show(value, nameof(FieldType.Whole)),
        _ => Show(value, nameof(FieldType.Number)),
    };

    /// <summary>Header text per column; two columns with the same name get their field appended so no column overwrites another.</summary>
    private static Dictionary<string, string> Headers(IReadOnlyList<ColumnDto> columns)
    {
        var names = columns.GroupBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return columns.ToDictionary(c => c.Field, c => names.Contains(c.DisplayName) ? $"{c.DisplayName} ({c.Field})" : c.DisplayName, StringComparer.Ordinal);
    }

    // ---- CSV

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static string BuildCsv(ReportResult r)
    {
        var sb = new StringBuilder();
        if (r.Rows.Count == 0 && r.Cards.Count > 0)
        {
            sb.Append("KPI,Value,Unit,Previous period,Same period last year,Numerator,Denominator,Calculation version\r\n");
            foreach (var c in r.Cards)
            {
                sb.AppendJoin(',', new[] { c.Name, Cell(c.Value), c.Unit, Cell(c.Previous), Cell(c.SamePeriodLastYear), Cell(c.Numerator), Cell(c.Denominator), c.CalculationVersion }.Select(Quote)).Append("\r\n");
            }

            return sb.ToString();
        }

        var headers = Headers(r.Columns);
        sb.AppendJoin(',', r.Columns.Select(c => Quote(headers[c.Field]))).Append("\r\n");
        foreach (var row in r.Rows)
        {
            sb.AppendJoin(',', r.Columns.Select(c => Quote(Convert.ToString(Raw(row.GetValueOrDefault(c.Field), c.DataType), CultureInfo.InvariantCulture) ?? string.Empty))).Append("\r\n");
        }

        return sb.ToString();

        static string Cell(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    // ---- Excel

    private static string SheetName(string title, HashSet<string> used)
    {
        var clean = new string(title.Where(c => !"[]:*?/\\".Contains(c, StringComparison.Ordinal)).ToArray()).Trim();
        clean = clean.Length == 0 ? "Sheet" : clean.Length > 28 ? clean[..28] : clean;
        var name = clean;
        for (var i = 2; !used.Add(name); i++)
        {
            name = $"{clean[..Math.Min(clean.Length, 25)]} {i}";
        }

        return name;
    }

    private static byte[] BuildXlsx(ReportResult r, DateTimeOffset generatedAt)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sheets = new Dictionary<string, object>();
        if (r.Rows.Count > 0)
        {
            var headers = Headers(r.Columns);
            sheets[SheetName("Report", used)] = r.Rows.Select(row => r.Columns.ToDictionary(c => headers[c.Field], c => Raw(row.GetValueOrDefault(c.Field), c.DataType), StringComparer.Ordinal)).ToList();
        }

        sheets[SheetName("Summary", used)] = SummaryRows(r, generatedAt);
        foreach (var s in r.Sections.Where(s => s.Table is { Count: > 0 }))
        {
            var cols = s.TableColumns ?? [];
            sheets[SheetName(s.Title, used)] = s.Table!.Select(row => cols.ToDictionary(c => c.Display, c => Raw(row.GetValueOrDefault(c.Field), c.Type.ToString()), StringComparer.Ordinal)).ToList();
        }

        using var stream = new MemoryStream();
        MiniExcel.SaveAs(stream, sheets);
        return stream.ToArray();
    }

    private static List<Dictionary<string, object?>> SummaryRows(ReportResult r, DateTimeOffset generatedAt)
    {
        var rows = new List<Dictionary<string, object?>>();
        void Add(string section, string item, object? value, string? note = null) => rows.Add(new Dictionary<string, object?> { ["Section"] = section, ["Item"] = item, ["Value"] = value is string s ? SafeText(s) : value, ["Note"] = note });
        Add("Report", "Name", r.ReportName);
        Add("Report", "Code", r.ReportCode);
        Add("Report", "Generated (IST)", generatedAt.ToOffset(Ist).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Add("Report", "Period", $"{r.Period.From:yyyy-MM-dd} to {r.Period.To:yyyy-MM-dd}");
        Add("Report", "Calculation version", r.CalculationVersion);
        Add("Report", "Data", r.DataSourceMode);
        Add("Report", "Rows", r.TotalRows);
        foreach (var (key, value) in r.FiltersApplied)
        {
            Add("Filters", key, value);
        }

        if (r.GroupedBy.Count > 0)
        {
            Add("Filters", "Grouped by", string.Join(", ", r.GroupedBy));
        }

        foreach (var c in r.Cards)
        {
            Add("KPIs", c.Name, c.Value, c.Measurable ? $"numerator {c.Numerator:0.##}, denominator {c.Denominator:0.##}; previous {c.Previous?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a"}; last year {c.SamePeriodLastYear?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a"}; version {c.CalculationVersion}" : c.Note);
        }

        foreach (var t in r.Totals)
        {
            Add("Totals", t.Label, t.Value);
        }

        foreach (var s in r.Sections)
        {
            foreach (var i in s.Items)
            {
                Add(s.Title, i.Label, Raw(i.Value, i.Format ?? string.Empty));
            }
        }

        foreach (var n in r.Notes)
        {
            Add("Notes", string.Empty, n);
        }

        return rows;
    }

    // ---- PDF

    private static byte[] BuildPdf(ReportResult r, DateTimeOffset generatedAt)
    {
        var pdf = new ReportPdf($"{r.ReportName} - generated {generatedAt.ToOffset(Ist):dd-MMM-yyyy HH:mm} IST - calculation version {r.CalculationVersion}");
        var filters = r.FiltersApplied.Count == 0 ? "no filters" : string.Join(", ", r.FiltersApplied.Select(f => $"{f.Key}: {f.Value}"));
        pdf.Title(r.ReportName, $"{r.Period.From:dd-MMM-yyyy} to {r.Period.To:dd-MMM-yyyy}  |  {filters}{(r.GroupedBy.Count > 0 ? $"  |  grouped by {string.Join(", ", r.GroupedBy)}" : string.Empty)}");
        if (r.Cards.Count > 0)
        {
            pdf.Heading("Key figures");
            pdf.Pairs(r.Cards.Select(c => (c.Name, c.Measurable ? $"{ShowUnit(c.Value, c.Unit)}{(c.Change is { } ch ? $"  ({(ch > 0 ? "+" : string.Empty)}{ch:0.##} vs previous)" : string.Empty)}" : "Not measurable")).ToList(), 4);
        }

        if (r.Totals.Count > 0)
        {
            pdf.Heading("Totals");
            pdf.Pairs(r.Totals.Select(t => (t.Label, ShowUnit(t.Value, t.Unit))).ToList(), 4);
        }

        foreach (var s in r.Sections)
        {
            pdf.Heading(s.Title);
            if (s.Items.Count > 0)
            {
                pdf.Pairs(s.Items.Select(i => (i.Label, i.Value is null ? "-" : Show(i.Value, i.Format switch { "currency" => nameof(FieldType.Currency), "percent" => nameof(FieldType.Percent), "number" => nameof(FieldType.Number), "date" => nameof(FieldType.Date), _ => string.Empty }))).ToList(), 4);
            }

            if (s.Table is { Count: > 0 } && s.TableColumns is { } cols)
            {
                pdf.Table(cols.Select(c => c.Display).ToList(), s.Table.Take(200).Select(row => (IReadOnlyList<string>)cols.Select(c => Show(row.GetValueOrDefault(c.Field), c.Type.ToString())).ToList()).ToList(), cols.Select(c => c.Type is FieldType.Currency or FieldType.Number or FieldType.Whole or FieldType.Percent).ToList());
            }
        }

        if (r.Rows.Count > 0)
        {
            pdf.Heading(r.Rows.Count > PdfRowLimit ? $"Detail (first {PdfRowLimit:N0} of {r.TotalRows:N0} rows - use Excel or CSV for all)" : $"Detail ({r.TotalRows:N0} rows)");
            pdf.Table(r.Columns.Select(c => c.DisplayName).ToList(), r.Rows.Take(PdfRowLimit).Select(row => (IReadOnlyList<string>)r.Columns.Select(c => Show(row.GetValueOrDefault(c.Field), c.DataType)).ToList()).ToList(),
                r.Columns.Select(c => c.DataType is nameof(FieldType.Currency) or nameof(FieldType.Number) or nameof(FieldType.Whole) or nameof(FieldType.Percent)).ToList());
        }

        if (r.Notes.Count > 0)
        {
            pdf.Heading("Notes");
            foreach (var n in r.Notes)
            {
                pdf.Line(n);
            }
        }

        if (r.Charts.Count > 0)
        {
            pdf.Line("Charts are shown on screen; the figures behind them are in this file and in the Excel export.", 7.5);
        }

        return pdf.ToBytes();
    }
}
