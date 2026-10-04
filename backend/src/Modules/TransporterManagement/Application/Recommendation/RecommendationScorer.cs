using System.Globalization;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Recommendation;

/// <summary>
/// Ranks eligible transporters with configurable weights. Every component is visible, every score has a basis,
/// and the cheapest carrier is not automatically recommended.
/// </summary>
public static class RecommendationScorer
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
        IReadOnlyList<CandidateEvaluation> eligible,
        RecommendationWeightsSetting weights,
        RecommendationScoringSetting scoring,
        int minimumSample)
    {
        if (eligible.Count == 0)
        {
            return [];
        }

        var costs = eligible.Where(c => c.Rate?.EstimatedCost is > 0).Select(c => c.Rate!.EstimatedCost!.Value).ToList();
        var cheapest = costs.Count > 0 ? costs.Min() : (decimal?)null;

        var scored = eligible
            .Select(c => Score(c, weights, scoring, minimumSample, cheapest))
            .OrderByDescending(s => s.Total)
            .ThenBy(s => s.Candidate.Rate?.EstimatedCost ?? decimal.MaxValue)
            .ThenBy(s => s.Candidate.Code, StringComparer.Ordinal)
            .ToList();

        var ranked = new List<RankedCandidate>();
        for (var i = 0; i < scored.Count; i++)
        {
            var item = scored[i];
            var comparisons = i == 0 ? [] : Compare(scored[0], item);
            ranked.Add(new RankedCandidate(i + 1, item.Candidate, item.Total, item.Components, Explain(item, scoring), comparisons));
        }

        return ranked;
    }

    private sealed record Scored(CandidateEvaluation Candidate, decimal Total, List<ScoreComponent> Components, decimal InsufficientScore, decimal PreferredBonus);

    private static Scored Score(
        CandidateEvaluation c,
        RecommendationWeightsSetting w,
        RecommendationScoringSetting s,
        int minimumSample,
        decimal? cheapest)
    {
        var components = new List<(string Factor, decimal? Score, decimal Weight, string Basis, bool Sufficient)>();

        // Rate: the cheapest eligible carrier scores 100, others in proportion to their cost.
        var cost = c.Rate?.EstimatedCost;
        if (cost is > 0 && cheapest is { } min)
        {
            components.Add((RateFactor, Math.Round(100m * min / cost.Value, 1), w.Rate,
                $"Estimated {Money(cost.Value)}", true));
        }
        else
        {
            components.Add((RateFactor, null, w.Rate,
                c.Rate?.Note ?? "No rate could be estimated", false));
        }

        // Performance: lane-level or transporter-level values, never a silent zero.
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
            components.Add(point.Sufficient && point.Value is { } value
                ? (factor, Math.Clamp(value, 0, 100), performanceWeights[type], $"{ScopeName(point.Scope)} {value:0.0}% (n={point.Denominator:0})", true)
                : (factor, null, performanceWeights[type], InsufficientBasis(point, minimumSample), false));
        }

        var claims = c.Kpis[KpiType.ClaimsRate];
        components.Add(claims.Sufficient && claims.Value is { } claimsValue
            ? ("Claims", Math.Round(100m * (1 - Math.Min(claimsValue, s.ClaimsZeroAtPct) / s.ClaimsZeroAtPct), 1), w.ClaimsRate,
                $"{claimsValue:0.0}% claims ({ScopeName(claims.Scope)})", true)
            : ("Claims", null, w.ClaimsRate, InsufficientBasis(claims, minimumSample), false));

        var available = Math.Min(100m, s.AvailabilityPerVehicle * c.AvailableVehicles);
        components.Add((AvailabilityFactor, available, w.Availability,
            $"{c.AvailableVehicles} vehicle(s) available", true));

        var totalWeight = components.Sum(x => x.Weight);
        var final = components
            .Select(x =>
            {
                var score = x.Score ?? s.InsufficientDataScore;
                var contribution = totalWeight == 0 ? 0 : x.Weight * score / totalWeight;
                return new ScoreComponent(x.Factor, Math.Round(score, 1), x.Weight, Math.Round(contribution, 2), x.Basis, x.Sufficient);
            })
            .ToList();

        var weighted = final.Sum(x => x.Contribution);
        var bonus = c.Preferred ? s.PreferredBonus : 0m;
        var total = Math.Round(Math.Min(100m, weighted + bonus), 1);

        return new Scored(c, total, final, s.InsufficientDataScore, bonus);
    }

    private static List<string> Explain(Scored item, RecommendationScoringSetting scoring)
    {
        var lines = new List<string>();

        foreach (var top in item.Components.Where(x => x.Sufficient).OrderByDescending(x => x.Contribution).Take(3))
        {
            lines.Add($"{top.Factor}: {top.Basis}");
        }

        if (item.Candidate.Preferred)
        {
            lines.Add($"Preferred carrier for this lane (+{item.PreferredBonus:0.#} points).");
        }

        var insufficient = item.Components.Where(x => !x.Sufficient).Select(x => x.Factor).ToList();
        if (insufficient.Count > 0)
        {
            lines.Add($"Not enough data for {string.Join(", ", insufficient)}; scored neutrally ({scoring.InsufficientDataScore:0} points), not as a failure.");
        }

        lines.AddRange(item.Candidate.Warnings);
        return lines;
    }

    /// <summary>
    /// Explains why a lower-ranked carrier is not preferred, in terms of the factor where the recommended carrier
    /// is strongest. Only factors that both carriers were scored on are compared.
    /// </summary>
    private static List<string> Compare(Scored top, Scored other)
    {
        var comparisons = new List<string>();
        var name = other.Candidate.Name;

        // The two factors where the recommended carrier is strongest, weighted by how much each factor matters.
        var gaps = top.Components
            .Join(other.Components, t => t.Factor, o => o.Factor, (t, o) => (t, o))
            .Where(p => p.t.Sufficient && p.o.Sufficient && p.t.Score > p.o.Score)
            .OrderByDescending(p => p.t.Weight * (p.t.Score - p.o.Score))
            .Take(2)
            .ToList();

        var topCost = top.Candidate.Rate?.EstimatedCost;
        var otherCost = other.Candidate.Rate?.EstimatedCost;

        if (topCost is { } tc && otherCost is { } oc && oc < tc)
        {
            comparisons.Add($"{name} has a lower rate by {Money(tc - oc)}, but scores {top.Total - other.Total:0.0} points lower overall.");
        }

        foreach (var gap in gaps)
        {
            comparisons.Add($"{gap.t.Factor}: {top.Candidate.Name} {gap.t.Basis} vs {name} {gap.o.Basis}.");
        }

        if (other.Candidate.Preferred != top.Candidate.Preferred && !top.Candidate.Preferred)
        {
            comparisons.Add($"{name} is a preferred carrier for this lane.");
        }

        return comparisons;
    }

    private static string InsufficientBasis(KpiPoint point, int minimumSample) =>
        point.Denominator == 0
            ? "No data yet"
            : $"Insufficient data (n={point.Denominator:0}, minimum {minimumSample})";

    private static string ScopeName(string scope) => scope == "Lane" ? "Lane" : "Overall";

    private static string Money(decimal amount) =>
        "₹" + Math.Round(amount, 0).ToString("N0", CultureInfo.InvariantCulture);
}
