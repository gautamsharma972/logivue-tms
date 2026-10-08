namespace Tms.Modules.Reports.Domain;

/// <summary>Everything a report's code is given: the facts for the period, the filters, who is asking and how to compute a KPI card with its comparisons.</summary>
public sealed class ReportContext(ReportFacts facts, PeriodKind periodKind, CompareMode compare, string grain)
{
    public ReportFacts Facts { get; } = facts;

    public DateRange Range => Facts.Range;

    public ReportFilters Filters => Facts.Filters;

    public ReportSettings Settings => Facts.Settings;

    public ReportPrincipal Principal => Facts.Principal;

    public DateOnly Today => Facts.Today;

    public DateTimeOffset Now => Facts.Now;

    public PeriodKind PeriodKind { get; } = periodKind;

    public CompareMode Compare { get; } = compare;

    /// <summary>Day, week, month, quarter or year: how trends are bucketed.</summary>
    public string Grain { get; } = grain;

    /// <summary>Daily KPI parts kept by the aggregation job, used for trends when no filter or limit applies. Null = always calculate from the facts.</summary>
    public ITrendSource? Trends { get; init; }

    /// <summary>Names of the report's own columns, so a "rank by" request can only name a real one.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    public DateRange Previous => Periods.Compare(Range, CompareMode.PreviousPeriod, PeriodKind);

    public DateRange LastYear => Periods.Compare(Range, CompareMode.SamePeriodLastYear, PeriodKind);

    public async Task<KpiResult> KpiAsync(string code, ReportFacts? facts = null)
    {
        var kpi = KpiCatalogue.Find(code) ?? throw new InvalidOperationException($"KPI {code} is not defined.");
        return await KpiCatalogue.CalculateAsync(kpi, facts ?? Facts);
    }

    /// <summary>A KPI for this period with the previous period and the same period last year beside it.</summary>
    public async Task<KpiCard> CardAsync(string code, string? drillReport = null, IReadOnlyDictionary<string, string>? drillFilters = null, bool compare = true)
    {
        var kpi = KpiCatalogue.Find(code) ?? throw new InvalidOperationException($"KPI {code} is not defined.");
        var now = await KpiCatalogue.CalculateAsync(kpi, Facts);
        KpiResult? before = null;
        KpiResult? lastYear = null;
        if (compare)
        {
            before = await KpiCatalogue.CalculateAsync(kpi, Facts.ForPeriod(Previous));
            lastYear = await KpiCatalogue.CalculateAsync(kpi, Facts.ForPeriod(LastYear));
        }

        decimal? change = now.Value is { } v && before?.Value is { } p ? Math.Round(v - p, 2) : null;
        decimal? changePct = change is { } c && before?.Value is { } pv && pv != 0 && kpi.Unit != KpiUnit.Percent ? Math.Round(c / Math.Abs(pv) * 100m, 1) : null;
        return new KpiCard(
            now.Code, now.Name, now.Unit.ToString(), now.Value, now.Numerator, now.Denominator, now.Measurable, now.Note, before?.Value, lastYear?.Value, change, changePct,
            KpiMath.Trend(change), KpiMath.Assess(change, kpi.HigherIsBetter), now.CalculationVersion, kpi.HigherIsBetter, drillReport ?? kpi.DrillReport, drillFilters ?? kpi.DrillFilters);
    }

    /// <summary>The ranges of the last <paramref name="buckets"/> periods of this run's grain, oldest first, ending with the one that contains the end of the report period.</summary>
    public IReadOnlyList<(string Label, DateRange Range)> Buckets(int buckets) => BucketsFor(Range.To, Grain, buckets);

    public static IReadOnlyList<(string Label, DateRange Range)> BucketsFor(DateOnly end, string grain, int buckets)
    {
        var result = new List<(string, DateRange)>();
        var cursor = end;
        for (var i = 0; i < buckets; i++)
        {
            var r = grain.ToLowerInvariant() switch
            {
                "day" or "daily" => new DateRange(cursor, cursor),
                "week" or "weekly" => Periods.WeekOf(cursor),
                "quarter" or "quarterly" => Periods.QuarterOf(cursor),
                _ => new DateRange(new DateOnly(cursor.Year, cursor.Month, 1), new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1).AddDays(-1)),
            };
            result.Add((Periods.Bucket(r.From, grain), r));
            cursor = r.From.AddDays(-1);
        }

        result.Reverse();
        return result;
    }

    /// <summary>One row per bucket with each requested KPI's value, for a trend chart. A bucket where a KPI cannot be judged has no value (a gap), not zero.</summary>
    public async Task<IReadOnlyList<ReportRow>> TrendAsync(IEnumerable<string> kpiCodes, int buckets)
    {
        var codes = kpiCodes.ToList();
        var rows = new List<ReportRow>();
        foreach (var (label, range) in Buckets(buckets))
        {
            var facts = Facts.ForPeriod(range);
            var row = new ReportRow { ["bucket"] = label, ["from"] = range.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
            foreach (var code in codes)
            {
                var kpi = KpiCatalogue.Find(code) ?? throw new InvalidOperationException($"KPI {code} is not defined.");
                var kept = Trends is null ? null : await Trends.PartsAsync(code, range);
                row[code] = kept is { } parts ? KpiMath.Value(kpi.Unit, kpi.Aggregation, parts) : (await KpiCatalogue.CalculateAsync(kpi, facts)).Value;
            }

            rows.Add(row);
        }

        return rows;
    }
}

/// <summary>Daily numerators and denominators kept from earlier runs. Returns null unless every day of the range is held, so a trend never mixes kept and missing days.</summary>
public interface ITrendSource
{
    Task<KpiParts?> PartsAsync(string kpiCode, DateRange range);
}
