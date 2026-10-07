using System.Globalization;
using System.Text;
using MiniExcelLibs;
using MiniExcelLibs.Csv;

namespace Tms.Modules.Contracts.Application;

public sealed record SheetFile(byte[] Content, string ContentType, string FileName);

/// <summary>Reading and writing rate sheets and reports as Excel or CSV. Cells that start like a formula are written as text, so a sheet cannot run anything when opened.</summary>
internal static class Sheets
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Csv = "text/csv; charset=utf-8";
    public const long MaxBytes = 6 * 1024 * 1024;

    internal static string Safe(string value) => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    public static async Task<SheetFile> WriteAsync(string format, string name, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream();
            await MiniExcel.SaveAsAsync(stream, rows.Select(r => r.ToDictionary(kv => kv.Key, kv => kv.Value is string s ? Safe(s) : kv.Value)).ToList());
            return new SheetFile(stream.ToArray(), Xlsx, $"{name}.xlsx");
        }

        var sb = new StringBuilder();
        if (rows.Count > 0)
        {
            var columns = rows[0].Keys.ToList();
            sb.AppendJoin(',', columns.Select(Quote)).Append("\r\n");
            foreach (var row in rows)
            {
                sb.AppendJoin(',', columns.Select(c => Quote(row.GetValueOrDefault(c) switch
                {
                    null => string.Empty,
                    string s => Safe(s),
                    DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    var v => v.ToString() ?? string.Empty,
                }))).Append("\r\n");
            }
        }

        return new SheetFile(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString()), Csv, $"{name}.csv");
    }

    /// <summary>Reads the first sheet (or a CSV) as rows keyed by their header, every value as text. Headers are trimmed and matched without regard to case or spacing by the caller.</summary>
    public static async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new byte[2];
        var read = await stream.ReadAtLeastAsync(head, 2, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;
        var isZip = read == 2 && head[0] == 'P' && head[1] == 'K';

        var rows = new List<IReadOnlyDictionary<string, string?>>();
        var query = isZip ? MiniExcel.Query(stream, useHeaderRow: true) : MiniExcel.Query(stream, useHeaderRow: true, excelType: ExcelType.CSV, configuration: new CsvConfiguration { ReadEmptyStringAsNull = true });
        foreach (IDictionary<string, object?> raw in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in raw)
            {
                var text = value switch
                {
                    null => null,
                    DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    var v => v.ToString(),
                };
                row[Normalise(key)] = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            if (row.Values.Any(v => v is not null))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>"Weight From", "weight_from" and "WEIGHTFROM" are the same column.</summary>
    public static string Normalise(string header) => new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
