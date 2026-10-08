using System.Globalization;

namespace Tms.Modules.Reports.Domain;

/// <summary>Grouping, sorting and column filtering of report rows. Pure, so what a grouped figure means can be tested without a database.</summary>
public static class ReportShaper
{
    public static decimal? ToDecimal(object? value) => value switch
    {
        null => null,
        decimal d => d,
        int i => i,
        long l => l,
        double d => (decimal)d,
        float f => (decimal)f,
        short s => s,
        _ => decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
    };

    private static string Key(object? value) => value switch
    {
        null => "(none)",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(none)",
    };

    /// <summary>One row per distinct combination of <paramref name="dimensions"/>, with each measure combined as its definition says.</summary>
    public static List<ReportRow> Group(IEnumerable<ReportRow> rows, IReadOnlyList<string> dimensions, IReadOnlyList<MeasureSpec> measures)
    {
        var result = new List<ReportRow>();
        foreach (var group in rows.GroupBy(r => string.Join('\u001f', dimensions.Select(d => Key(r.GetValueOrDefault(d)))), StringComparer.Ordinal))
        {
            var members = group.ToList();
            var row = new ReportRow();
            foreach (var d in dimensions)
            {
                row[d] = members[0].GetValueOrDefault(d) ?? "(none)";
            }

            foreach (var m in measures.Where(m => m.Kind != MeasureKind.Ratio))
            {
                row[m.Field] = Combine(m, members);
            }

            foreach (var m in measures.Where(m => m.Kind == MeasureKind.Ratio))
            {
                var numerator = Sum(members, m.Numerator!);
                var denominator = Sum(members, m.Denominator!);
                row[m.Field] = denominator is > 0 && numerator is not null ? Math.Round(numerator.Value / denominator.Value * m.Scale, 2) : null;
            }

            result.Add(row);
        }

        return result;
    }

    private static decimal? Sum(List<ReportRow> members, string field)
    {
        var values = members.Select(r => ToDecimal(r.GetValueOrDefault(field))).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Sum();
    }

    private static object? Combine(MeasureSpec m, List<ReportRow> members)
    {
        if (m.Kind == MeasureKind.Count)
        {
            return members.Count;
        }

        var values = members.Select(r => ToDecimal(r.GetValueOrDefault(m.Field))).Where(v => v is not null).Select(v => v!.Value).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return m.Kind switch
        {
            MeasureKind.Sum => RoundIfWhole(values.Sum()),
            MeasureKind.Avg => Math.Round(values.Average(), 2),
            MeasureKind.Min => values.Min(),
            _ => values.Max(),
        };
    }

    private static decimal RoundIfWhole(decimal v) => Math.Round(v, 2);

    public static List<ReportRow> Sort(IEnumerable<ReportRow> rows, IReadOnlyList<(string Field, bool Descending)> sorts)
    {
        if (sorts.Count == 0)
        {
            return rows.ToList();
        }

        IOrderedEnumerable<ReportRow>? ordered = null;
        foreach (var (field, desc) in sorts)
        {
            var comparer = Comparer<object?>.Create(Compare);
            ordered = ordered is null
                ? (desc ? rows.OrderByDescending(r => r.GetValueOrDefault(field), comparer) : rows.OrderBy(r => r.GetValueOrDefault(field), comparer))
                : (desc ? ordered.ThenByDescending(r => r.GetValueOrDefault(field), comparer) : ordered.ThenBy(r => r.GetValueOrDefault(field), comparer));
        }

        return ordered!.ToList();
    }

    /// <summary>Order of two cell values: nothing sorts first ascending (so it is last when descending, but never mixed into the numbers).</summary>
    public static int Compare(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null ? 0 : a is null ? -1 : 1;
        }

        if (ToDecimal(a) is { } x && ToDecimal(b) is { } y && a is not string && b is not string)
        {
            return x.CompareTo(y);
        }

        return (a, b) switch
        {
            (DateOnly p, DateOnly q) => p.CompareTo(q),
            (DateTimeOffset p, DateTimeOffset q) => p.CompareTo(q),
            _ => string.Compare(Key(a), Key(b), StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>Keeps rows whose text columns contain the wanted text (case-insensitive) or, for numbers and dates, equal it.</summary>
    public static List<ReportRow> FilterColumns(IEnumerable<ReportRow> rows, IReadOnlyDictionary<string, string> wanted)
    {
        if (wanted.Count == 0)
        {
            return rows.ToList();
        }

        return rows.Where(r => wanted.All(w => r.TryGetValue(w.Key, out var value) && value is not null && Key(value).Contains(w.Value, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
