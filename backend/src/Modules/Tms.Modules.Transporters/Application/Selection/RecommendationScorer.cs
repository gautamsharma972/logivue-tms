using System.Globalization;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Application.Selection;

/// <summary>Ranks eligible transporters with configurable weights. Every component is visible, every score has a basis, and the cheapest carrier is not automatically recommended.</summary>
internal static class RecommendationScorer
{
    private const string RateFactor = "Rate";
    private const string AvailabilityFactor = "Availability";

    private static readonly (string Factor, KpiType Type)[] PerformanceFactors =
    [
        ("On-time pickup", KpiType.OnTimePickup),
        ("On-time delivery", KpiType.OnTimeDelivery),
        ("Placement compliance", KpiType.PlacementCompliance),
        ("POD compliance", KpiType.PodCompliance),
        ("Tender acceptance", KpiType.TenderAcceptance),
    ];

    public static IReadOnlyList<RankedCandidate> Rank(
        IReadOnlyList<CandidateEvaluation> eligible, RecommendationWeightsSetting weights, RecommendationScoringSetting scoring, int minimumSample)
    {
        if (eligible.Count == 0)
        {
            return [];
        }

        var costs = eligible.Where(c => c.Rate?.Total > 0).Select(c => c.Rate!.Total).ToList();
        decimal? cheapest = costs.Count > 0 ? costs.Min() : null;

        var scored = eligible.Select(c => Score(c, weights, scoring, minimumSample, cheapest))
            .OrderByDescending(s => s.Total).ThenBy(s => s.Candidate.Rate?.Total ?? decimal.MaxValue).ThenBy(s => s.Candidate.Code, StringComparer.Ordinal).ToList();

        var ranked = new List<RankedCandidate>();
        for (var i = 0; i < scored.Count; i++)
        {
            var item = scored[i];
            ranked.Add(new RankedCandidate(i + 1, item.Candidate, item.Total, item.Components, Explain(item, scoring), i == 0 ? [] : Compare(scored[0], item)));
        }

        return ranked;
    }

    private sealed record Scored(CandidateEvaluation Candidate, decimal Total, List<ScoreComponent> Components, decimal PreferredBonus);

    private static Scored Score(CandidateEvaluation c, RecommendationWeightsSetting w, RecommendationScoringSetting s, int minimumSample, decimal? cheapest)
    {
        var components = new List<(string Factor, decimal? Score, decimal Weight, string Basis, bool Sufficient)>();

        // Rate: the cheapest eligible carrier scores 100, others in proportion to their cost.
        if (c.Rate is { Total: > 0 } rate && cheapest is { } min)
        {
            components.Add((RateFactor, Math.Round(100m * min / rate.Total, 1), w.Rate, $"Estimated {Money(rate.Total)}", true));
        }
        else
        {
            components.Add((RateFactor, null, w.Rate, c.Rate?.Note ?? "No rate could be estimated", false));
        }

        var performanceWeights = new Dictionary<KpiType, decimal>
        {
            [KpiType.OnTimePickup] = w.OnTimePickup,
            [KpiType.OnTimeDelivery] = w.OnTimeDelivery,
            [KpiType.PlacementCompliance] = w.PlacementCompliance,
            [KpiType.PodCompliance] = w.PodCompliance,
            [KpiType.TenderAcceptance] = w.TenderAcceptance,
        };
        foreach (var (factor, type) in PerformanceFactors)
        {
            var point = c.Kpis[type];
            components.Add(point is { Sufficient: true, Value: { } value }
                ? (factor, Math.Clamp(value, 0, 100), performanceWeights[type], $"{ScopeName(point.Scope)} {value:0.0}% (n={point.Denominator:0})", true)
                : (factor, null, performanceWeights[type], InsufficientBasis(point, minimumSample), false));
        }

        var claims = c.Kpis[KpiType.ClaimsRate];
        components.Add(claims is { Sufficient: true, Value: { } claimsValue }
            ? ("Claims", Math.Round(100m * (1 - Math.Min(claimsValue, s.ClaimsZeroAtPct) / s.ClaimsZeroAtPct), 1), w.ClaimsRate, $"{claimsValue:0.0}% claims ({ScopeName(claims.Scope)})", true)
            : ("Claims", null, w.ClaimsRate, InsufficientBasis(claims, minimumSample), false));

        components.Add((AvailabilityFactor, Math.Min(100m, s.AvailabilityPerVehicle * c.AvailableVehicles), w.Availability, $"{c.AvailableVehicles} vehicle(s) available", true));

        var totalWeight = components.Sum(x => x.Weight);
        var final = components.Select(x =>
        {
            var score = x.Score ?? s.InsufficientDataScore;
            return new ScoreComponent(x.Factor, Math.Round(score, 1), x.Weight, Math.Round(totalWeight == 0 ? 0 : x.Weight * score / totalWeight, 2), x.Basis, x.Sufficient);
        }).ToList();

        var bonus = c.Preferred ? s.PreferredBonus : 0m;
        return new Scored(c, Math.Round(Math.Min(100m, final.Sum(x => x.Contribution) + bonus), 1), final, bonus);
    }

    private static List<string> Explain(Scored item, RecommendationScoringSetting scoring)
    {
        var lines = item.Components.Where(x => x.Sufficient).OrderByDescending(x => x.Contribution).Take(3).Select(x => $"{x.Factor}: {x.Basis}").ToList();
        if (item.Candidate.Preferred)
        {
            lines.Add($"Preferred carrier (+{item.PreferredBonus:0.#} points).");
        }

        var insufficient = item.Components.Where(x => !x.Sufficient).Select(x => x.Factor).ToList();
        if (insufficient.Count > 0)
        {
            lines.Add($"Not enough data for {string.Join(", ", insufficient)}; scored neutrally ({scoring.InsufficientDataScore:0} points), not as a failure.");
        }

        lines.AddRange(item.Candidate.Warnings);
        return lines;
    }

    /// <summary>Explains why a lower-ranked carrier is not preferred, in terms of the factors where the recommended carrier is strongest. Only factors both were scored on are compared.</summary>
    private static List<string> Compare(Scored top, Scored other)
    {
        var comparisons = new List<string>();
        var name = other.Candidate.Name;
        var gaps = top.Components.Join(other.Components, t => t.Factor, o => o.Factor, (t, o) => (t, o))
            .Where(p => p.t.Sufficient && p.o.Sufficient && p.t.Score > p.o.Score)
            .OrderByDescending(p => p.t.Weight * (p.t.Score - p.o.Score)).Take(2).ToList();

        if (top.Candidate.Rate?.Total is { } tc && other.Candidate.Rate?.Total is { } oc && oc < tc)
        {
            comparisons.Add($"{name} has a lower rate by {Money(tc - oc)}, but scores {top.Total - other.Total:0.0} points lower overall.");
        }

        comparisons.AddRange(gaps.Select(gap => $"{gap.t.Factor}: {top.Candidate.Name} {gap.t.Basis} vs {name} {gap.o.Basis}."));
        if (other.Candidate.Preferred && !top.Candidate.Preferred)
        {
            comparisons.Add($"{name} is a preferred carrier.");
        }

        return comparisons;
    }

    private static string InsufficientBasis(KpiPoint point, int minimumSample) =>
        point.Denominator == 0 ? "No data yet" : $"Insufficient data (n={point.Denominator:0}, minimum {minimumSample})";

    private static string ScopeName(string scope) => scope == "Lane" ? "Lane" : "Overall";

    private static string Money(decimal amount) => "₹" + Math.Round(amount, 0).ToString("N0", CultureInfo.InvariantCulture);
}
