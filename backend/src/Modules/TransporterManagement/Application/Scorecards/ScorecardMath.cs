using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Performance;

namespace LogiVue.Tms.TransporterManagement.Application.Scorecards;

/// <summary>One KPI pooled over a period, with its score and weighted contribution.</summary>
/// <param name="Value">Pooled percentage. Null when the sample is below the minimum.</param>
/// <param name="Score">0-100, higher is better. Null when there is no value.</param>
public sealed record MeasuredKpi(
    KpiType Kpi,
    decimal Weight,
    decimal Numerator,
    decimal Denominator,
    decimal? Value,
    decimal? Score,
    decimal? WeightedScore);

/// <summary>
/// The scoring rules shared by scorecards and rankings. Pure, so the same numbers come out wherever they are shown.
/// A KPI with too small a sample is excluded and the remaining weights are renormalised, so missing data neither
/// rewards nor penalises a transporter.
/// </summary>
public static class ScorecardMath
{
    /// <summary>KPIs that are better when lower. Claims rate is the only one.</summary>
    public static bool LowerIsBetter(KpiType kpi) => kpi == KpiType.ClaimsRate;

    /// <summary>Pools stored KPI rows by type. Rows of other types are ignored.</summary>
    public static decimal? Pooled(IEnumerable<PerformanceKpi> rows, KpiType kpi, int minimumSample, out decimal numerator, out decimal denominator)
    {
        var typeRows = rows.Where(k => k.KpiType == kpi).ToList();
        numerator = typeRows.Sum(k => k.Numerator);
        denominator = typeRows.Sum(k => k.Denominator);
        return denominator > 0 && denominator >= minimumSample ? Math.Round(numerator * 100m / denominator, 2) : null;
    }

    public static IReadOnlyList<MeasuredKpi> Measure(IEnumerable<PerformanceKpi> rows, KpiWeightsSetting weights, int minimumSample, RecommendationScoringSetting scoring)
    {
        var rowList = rows as IReadOnlyCollection<PerformanceKpi> ?? rows.ToList();
        var measured = new List<(KpiType Kpi, decimal Weight, decimal Numerator, decimal Denominator, decimal? Value, decimal? Score)>();
        foreach (var (kpi, weight) in WeightsFor(weights))
        {
            if (weight <= 0)
            {
                continue;
            }

            var value = Pooled(rowList, kpi, minimumSample, out var numerator, out var denominator);
            measured.Add((kpi, weight, numerator, denominator, value, value is null ? null : ToScore(kpi, value.Value, scoring)));
        }

        var totalWeight = measured.Where(m => m.Score is not null).Sum(m => m.Weight);
        return measured.Select(m => new MeasuredKpi(m.Kpi, m.Weight, m.Numerator, m.Denominator, m.Value, m.Score,
            m.Score is { } v && totalWeight > 0 ? Math.Round(m.Weight * v / totalWeight, 2) : null)).ToList();
    }

    /// <summary>Weighted average of the measured KPIs. Null when nothing is measurable.</summary>
    public static decimal? Overall(IEnumerable<MeasuredKpi> kpis)
    {
        var scored = kpis.Where(k => k.Score is not null).ToList();
        var totalWeight = scored.Sum(k => k.Weight);
        return totalWeight > 0 ? Math.Round(scored.Sum(k => k.Weight * k.Score!.Value) / totalWeight, 2) : null;
    }

    /// <summary>
    /// Converts a KPI value to a 0-100 score where higher is better. Claims are lower-is-better: the score falls to zero
    /// at the claims rate that the recommendation scoring treats as the worst case.
    /// </summary>
    public static decimal ToScore(KpiType kpi, decimal value, RecommendationScoringSetting scoring) =>
        kpi == KpiType.ClaimsRate
            ? Math.Round(Math.Clamp(100m - value / scoring.ClaimsZeroAtPct * 100m, 0m, 100m), 2)
            : Math.Clamp(value, 0m, 100m);

    public static IEnumerable<(KpiType Kpi, decimal Weight)> WeightsFor(KpiWeightsSetting w) =>
    [
        (KpiType.OnTimePickup, w.OnTimePickup),
        (KpiType.OnTimeDelivery, w.OnTimeDelivery),
        (KpiType.PlacementCompliance, w.PlacementCompliance),
        (KpiType.TenderAcceptance, w.TenderAcceptance),
        (KpiType.PodCompliance, w.PodCompliance),
        (KpiType.ClaimsRate, w.ClaimsRate),
        (KpiType.CostPerformance, w.CostPerformance),
        (KpiType.Availability, w.Availability)
    ];
}
