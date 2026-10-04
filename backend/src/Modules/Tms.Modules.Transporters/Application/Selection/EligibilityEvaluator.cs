using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Transporters.Application.Selection;

/// <summary>
/// Pure eligibility rules. Every failed rule adds a human-readable reason, so a transporter's exclusion is always explainable.
/// The same rules that bar a transporter from a load also bar it from planning: <see cref="Standing"/> is the part planning uses.
/// </summary>
public static class EligibilityEvaluator
{
    public static IReadOnlyList<CandidateEvaluation> Evaluate(SelectionRequest request, IReadOnlyList<CandidateData> candidates, EligibilityParameters parameters) =>
        candidates.Select(c => EvaluateOne(request, c, parameters))
            .OrderByDescending(r => r.Eligible).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();

    private static CandidateEvaluation EvaluateOne(SelectionRequest request, CandidateData data, EligibilityParameters p)
    {
        var transporter = data.Transporter;
        var reasons = new List<string>();
        var warnings = new List<string>();
        var date = request.Date;

        // 1. The standing rules shared with planning: status, documents, planning rules, urgency.
        var lane = FindLane(data.Lanes, request);
        var standing = Standing(transporter.Status, data.DocumentIssues, data.Rules, lane?.Id, request.IsUrgent, date);
        reasons.AddRange(standing.Reasons);
        warnings.AddRange(standing.Warnings);

        // 2. Lane serviceability.
        if (lane is null)
        {
            reasons.Add($"Lane not configured for {Place(request.OriginCity, request.OriginState)} to {Place(request.DestinationCity, request.DestinationState)} ({(request.Mode == FreightMode.Ftl ? "full truck" : "part load")}) on {date:yyyy-MM-dd}.");
        }
        else if (lane.TransitSlaMinutes is { } sla && request.PickupBy is { } pickup && request.DeliverBy is { } deliver && (deliver - pickup).TotalMinutes < sla)
        {
            reasons.Add($"Committed transit of {sla / 60.0:0.#} h exceeds the {(deliver - pickup).TotalHours:0.#} h between pickup and delivery.");
        }

        // 3. Required capabilities.
        var held = data.Capabilities.Where(c => c.HeldOn(date)).Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var missing in (request.RequiredCapabilities ?? []).Select(c => c.Trim().ToUpperInvariant()).Distinct().Where(c => !held.Contains(c)))
        {
            reasons.Add($"Required capability {missing} is not held.");
        }

        // 4. Vehicles: in service that day, papers in order, of the type, big enough.
        var usable = data.Vehicles.Where(v => v.IsAvailableOn(date) && v.Compliance != FleetCompliance.NonCompliant).ToList();
        var blocked = data.Vehicles.Count(v => v.Compliance == FleetCompliance.NonCompliant);
        if (blocked > 0)
        {
            warnings.Add($"{blocked} vehicle(s) have papers that are not in order and are left out.");
        }

        var ofType = usable.Where(v => request.VehicleTypeId is null || v.VehicleTypeId == request.VehicleTypeId).ToList();
        var fit = ofType.Where(v => v.PayloadKg >= request.WeightKg).ToList();
        if (ofType.Count == 0)
        {
            reasons.Add(request.VehicleTypeId is null ? "No vehicle is available." : "No available vehicle of the required type.");
        }
        else if (fit.Count == 0)
        {
            reasons.Add($"No available vehicle has capacity for {request.WeightKg:0.#} kg (largest available {ofType.Max(v => v.PayloadKg):0.#} kg).");
        }

        // 5. Commercial terms: the contract engine must be able to price the load for this transporter.
        if (data.Rate is null)
        {
            reasons.Add($"No contract rate applies to this load on {date:yyyy-MM-dd}.");
        }

        // 6. Performance restrictions (optional, configured).
        var kpis = Enum.GetValues<KpiType>().ToDictionary(type => type, type => SelectKpi(data.Kpis, type, lane?.Id, date, p.PerformanceWindowDays, p.MinimumSample));
        reasons.AddRange(PerformanceReasons(kpis, p.Restrictions));

        return new CandidateEvaluation(
            transporter.Id, transporter.Code, transporter.LegalName, transporter.Status, reasons.Count == 0, reasons, warnings, lane?.Id,
            standing.Preferred, standing.Restricted, data.Rate, fit.Count, kpis);
    }

    /// <summary>The reasons a transporter's measured performance bars it, when performance limits are switched on. Judged only on KPIs with enough loads.</summary>
    public static IReadOnlyList<string> PerformanceReasons(IReadOnlyDictionary<KpiType, KpiPoint> kpis, Settings.EligibilityRestrictionsSetting restrictions)
    {
        var reasons = new List<string>();
        if (!restrictions.Enabled)
        {
            return reasons;
        }

        if (kpis[KpiType.OnTimeDelivery] is { Sufficient: true, Value: { } otd } && otd < restrictions.MinOtdPct)
        {
            reasons.Add($"On-time delivery of {otd:0.0}% is below the minimum of {restrictions.MinOtdPct:0.#}%.");
        }

        if (kpis[KpiType.ClaimsRate] is { Sufficient: true, Value: { } claims } && claims > restrictions.MaxClaimsRatePct)
        {
            reasons.Add($"Claims rate of {claims:0.0}% exceeds the maximum of {restrictions.MaxClaimsRatePct:0.#}%.");
        }

        return reasons;
    }

    /// <summary>The outcome of the rules that decide whether a transporter may be given a load at all, whatever the load is.</summary>
    public sealed record StandingResult(IReadOnlyList<string> Reasons, IReadOnlyList<string> Warnings, bool Preferred, bool Restricted)
    {
        public bool Allowed => Reasons.Count == 0;
    }

    public static StandingResult Standing(
        TransporterStatus status, IReadOnlyList<DocumentIssue> documentIssues, IReadOnlyList<PlanningRule> rules, Guid? laneId, bool urgent, DateOnly date)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();

        if (status != TransporterStatus.Active)
        {
            reasons.Add(status switch
            {
                TransporterStatus.Suspended => "Transporter is suspended.",
                TransporterStatus.Rejected => "Transporter's onboarding was rejected.",
                _ => $"Transporter is {status}, not active.",
            });
        }

        reasons.AddRange(documentIssues.Where(i => i.Blocks).Select(i => $"Compliance: {i.Message}"));
        warnings.AddRange(documentIssues.Where(i => !i.Blocks).Select(i => $"Compliance: {i.Message}"));

        var active = rules.Where(r => r.AppliesOn(date)).ToList();
        foreach (var rule in active.Where(r => r.IsExclusion && (r.LaneId is null || r.LaneId == laneId)))
        {
            reasons.Add($"{(rule.LaneId is null ? "Restricted" : "Restricted on this lane")} by a planning rule ({rule.RuleType}): {rule.Reason}");
        }

        if (urgent && active.Any(r => r.RuleType == PlanningRuleType.AvoidForUrgent && (r.LaneId is null || r.LaneId == laneId)))
        {
            reasons.Add("Avoided for urgent loads by a planning rule.");
        }

        var preferred = active.Any(r => r.RuleType == PlanningRuleType.PreferredCarrier && r.LaneId is null)
            || (laneId is not null && active.Any(r => r.RuleType == PlanningRuleType.PreferredLane && r.LaneId == laneId));
        var restricted = active.Any(r => r.IsExclusion && (r.LaneId is null || r.LaneId == laneId));
        return new StandingResult(reasons, warnings, preferred, restricted);
    }

    /// <summary>The lane the load runs on: in service that day, matching the places and service, with the shortest committed transit first.</summary>
    public static TransporterLane? FindLane(IEnumerable<TransporterLane> lanes, SelectionRequest request) =>
        lanes.Where(l => l.InServiceOn(request.Date) && l.Covers(request.OriginState, request.OriginCity, request.DestinationState, request.DestinationCity, request.Mode))
            .OrderBy(l => l.OriginCity is null ? 1 : 0).ThenBy(l => l.DestinationCity is null ? 1 : 0).ThenBy(l => l.TransitSlaMinutes ?? int.MaxValue).FirstOrDefault();

    /// <summary>
    /// Picks the KPI to score with. Monthly buckets in the window are pooled (sum of numerators over sum of denominators). A lane-level value wins
    /// when its pooled sample is large enough; otherwise the transporter-wide value is used; otherwise the KPI is reported as insufficient, never as zero.
    /// </summary>
    public static KpiPoint SelectKpi(IEnumerable<PerformanceKpi> rows, KpiType type, Guid? laneId, DateOnly asOf, int windowDays, int minimumSample)
    {
        var window = rows.Where(k => k.KpiType == type && k.VehicleTypeId is null && k.PeriodEnd >= asOf.AddDays(-windowDays) && k.PeriodStart <= asOf).ToList();
        var lane = laneId is { } id ? Pool(window.Where(k => k.LaneId == id)) : (null, 0m);
        if (lane.Value is { } laneValue && lane.Observed >= minimumSample)
        {
            return new KpiPoint(laneValue, lane.Observed, true, "Lane");
        }

        var transporter = Pool(window.Where(k => k.LaneId is null));
        if (transporter.Value is { } value && transporter.Observed >= minimumSample)
        {
            return new KpiPoint(value, transporter.Observed, true, "Transporter");
        }

        return new KpiPoint(null, Math.Max(lane.Observed, transporter.Observed), false, "None");
    }

    private static (decimal? Value, decimal Observed) Pool(IEnumerable<PerformanceKpi> rows)
    {
        var list = rows.ToList();
        var observed = list.Sum(k => k.Denominator);
        return observed == 0 ? (null, 0m) : (Math.Round(list.Sum(k => k.Numerator) * 100m / observed, 2), observed);
    }

    private static string Place(string? city, string state) => city is null ? state : $"{city}, {state}";
}
