namespace Tms.Modules.Reports.Domain;

public enum KpiUnit { Percent, Count, Currency, Number, Minutes, Ratio }

public enum KpiAggregation { Percent, Sum, Average, Count }

/// <summary>What went into a KPI. <see cref="Excluded"/> are facts left out because the number cannot be judged for them (no planned time, proof not required): they are never counted as failures.</summary>
public readonly record struct KpiParts(decimal Numerator, decimal Denominator, int Excluded = 0, string? ExcludedReason = null);

/// <summary>A KPI's definition and the calculation that gives its parts. The definition is also stored (and shown) as data; the calculation is versioned code.</summary>
public sealed class KpiDefinition
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Formula { get; init; }

    public required KpiUnit Unit { get; init; }

    public required string Numerator { get; init; }

    public required string Denominator { get; init; }

    public required KpiAggregation Aggregation { get; init; }

    public required string Module { get; init; }

    public required string SourceOfTruth { get; init; }

    public string Version { get; init; } = "1.0";

    public bool HigherIsBetter { get; init; } = true;

    /// <summary>Which report a click on this KPI opens, and the filters it adds there.</summary>
    public string? DrillReport { get; init; }

    public IReadOnlyDictionary<string, string>? DrillFilters { get; init; }

    public required Func<ReportFacts, Task<KpiParts>> Evaluate { get; init; }
}

/// <summary>One KPI for one period: the parts, the value, and whether it can be judged at all.</summary>
public sealed record KpiResult(
    string Code,
    string Name,
    KpiUnit Unit,
    decimal Numerator,
    decimal Denominator,
    decimal? Value,
    bool Measurable,
    int Excluded,
    string? Note,
    DateOnly From,
    DateOnly To,
    string CalculationVersion,
    bool HigherIsBetter);

public static class KpiMath
{
    /// <summary>Value from parts. Not measurable (null) when nothing could be judged: never zero.</summary>
    public static decimal? Value(KpiUnit unit, KpiAggregation aggregation, KpiParts p)
    {
        if (p.Denominator <= 0)
        {
            return null;
        }

        var raw = aggregation switch
        {
            KpiAggregation.Percent => p.Numerator / p.Denominator * 100m,
            KpiAggregation.Average => p.Numerator / p.Denominator,
            _ => p.Numerator,
        };
        return Math.Round(raw, unit is KpiUnit.Currency or KpiUnit.Number or KpiUnit.Ratio ? 2 : unit == KpiUnit.Percent ? 1 : 0, MidpointRounding.AwayFromZero);
    }

    public static string Trend(decimal? change) => change switch
    {
        null => "none",
        > 0 => "up",
        < 0 => "down",
        _ => "flat",
    };

    /// <summary>Good, Bad or Neutral for a change, given which direction is better.</summary>
    public static string Assess(decimal? change, bool higherIsBetter) => change switch
    {
        null or 0 => "neutral",
        > 0 => higherIsBetter ? "good" : "bad",
        _ => higherIsBetter ? "bad" : "good",
    };
}
