using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Eligibility;

/// <summary>
/// Pure eligibility rules. Every failed rule adds a human-readable reason, so a transporter's exclusion is always explainable.
/// </summary>
public static class TransporterEligibilityEvaluator
{
    public static IReadOnlyList<CandidateEvaluation> Evaluate(TransporterSelectionRequest request, EligibilityData data, EligibilityParameters parameters)
    {
        var documents = data.Documents.ToLookup(d => d.TransporterId);
        var vehicles = data.Vehicles.ToLookup(v => v.TransporterId);
        var lanes = data.Lanes.ToLookup(l => l.TransporterId);
        var rates = data.Rates.ToLookup(r => r.TransporterId);
        var capabilities = data.Capabilities.ToLookup(c => c.TransporterId);
        var rules = data.PlanningRules.ToLookup(r => r.TransporterId);
        var kpis = data.Kpis.ToLookup(k => k.TransporterId);

        var results = data.Transporters
            .Select(t => EvaluateOne(t, request, data, parameters, documents[t.Id].ToList(), vehicles[t.Id].ToList(),
                lanes[t.Id].ToList(), rates[t.Id].ToList(), capabilities[t.Id].ToList(), rules[t.Id].ToList(), kpis[t.Id].ToList()))
            .OrderByDescending(r => r.Eligible)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

        return results;
    }

    private static CandidateEvaluation EvaluateOne(
        Transporter transporter,
        TransporterSelectionRequest request,
        EligibilityData data,
        EligibilityParameters p,
        List<TransporterDocument> docs,
        List<TransporterVehicle> fleet,
        List<TransporterLane> transporterLanes,
        List<TransporterRate> transporterRates,
        List<CapabilityHolding> transporterCapabilities,
        List<TransporterPlanningRule> transporterRules,
        List<PerformanceKpi> transporterKpis)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();
        var asOf = p.RequestDate;
        var asOfDt = asOf.ToDateTime(TimeOnly.MinValue);
        var service = request.ServiceType.Trim().ToUpperInvariant();
        var origin = request.OriginLocationId;
        var destination = request.DestinationLocationId;

        // 1. Transporter status
        if (transporter.Status != TransporterStatus.Active)
        {
            reasons.Add(StatusReason(transporter.Status));
        }

        // 2. Compliance
        var report = ComplianceEvaluator.Evaluate(transporter.Id, p.ComplianceDate, p.DefaultReminderDays, data.DocumentTypes, docs, fleet);
        foreach (var item in report.Items.Where(i => i.Scope == ComplianceScope.Transporter && (i.BlocksApproval || i.BlocksAllocation)))
        {
            reasons.Add($"Compliance: {item.Message}");
        }

        foreach (var item in report.Items.Where(i => i.Scope == ComplianceScope.Transporter && i.State == ComplianceItemState.ExpiringSoon))
        {
            warnings.Add($"Compliance: {item.Message}");
        }

        var blockedVehicles = report.BlockedVehicleIds.ToHashSet();
        if (blockedVehicles.Count > 0)
        {
            warnings.Add($"{blockedVehicles.Count} vehicle(s) are blocked by compliance and excluded from allocation.");
        }

        // 3. Planning classifications (active on the request date)
        var activeRules = transporterRules
            .Where(r => r.IsActive && r.EffectiveFrom <= asOfDt && (r.EffectiveTo == null || r.EffectiveTo >= asOfDt))
            .ToList();

        foreach (var rule in activeRules.Where(r => r.LaneReference == null && IsExclusion(r.RuleType)))
        {
            reasons.Add($"Restricted by planning rule ({rule.RuleType}): {rule.Reason}");
        }

        var preferredCarrier = activeRules.Any(r => r.LaneReference == null && r.RuleType == PlanningRuleType.PreferredCarrier);
        if (request.IsUrgent && activeRules.Any(r => r.LaneReference == null && r.RuleType == PlanningRuleType.AvoidForUrgent))
        {
            reasons.Add("Avoided for urgent loads by a planning rule.");
        }

        // 4. Lane serviceability
        var lane = transporterLanes
            .Where(l => l.Status == RecordStatus.Active
                && l.OriginLocationReference == origin
                && l.DestinationLocationReference == destination
                && l.ServiceType == service
                && (l.VehicleTypeReference == null || request.VehicleTypeId == null || l.VehicleTypeReference == request.VehicleTypeId)
                && l.EffectiveFrom <= asOfDt
                && (l.EffectiveTo == null || l.EffectiveTo >= asOfDt))
            .OrderBy(l => l.VehicleTypeReference == null ? 1 : 0)
            .ThenBy(l => l.TransitSlaMinutes ?? int.MaxValue)
            .FirstOrDefault();

        var preferredLane = false;
        if (lane is null)
        {
            reasons.Add($"Lane not configured for {origin} to {destination} ({service}) on {asOf:yyyy-MM-dd}.");
        }
        else
        {
            foreach (var rule in activeRules.Where(r => r.LaneReference == lane.Id && IsExclusion(r.RuleType)))
            {
                reasons.Add($"Restricted on this lane by planning rule ({rule.RuleType}): {rule.Reason}");
            }

            preferredLane = activeRules.Any(r => r.LaneReference == lane.Id && r.RuleType == PlanningRuleType.PreferredLane);

            if (lane.TransitSlaMinutes is { } sla && request.PickupBy is { } pickup && request.DeliverBy is { } deliver)
            {
                var windowMinutes = (deliver - pickup).TotalMinutes;
                if (windowMinutes < sla)
                {
                    reasons.Add($"Transit SLA of {sla / 60.0:0.#} h exceeds the {windowMinutes / 60.0:0.#} h between pickup and delivery.");
                }
            }
        }

        // 5. Required capabilities
        var held = transporterCapabilities
            .Where(c => c.Status == RecordStatus.Active && c.EffectiveFrom <= asOfDt && (c.EffectiveTo == null || c.EffectiveTo >= asOfDt))
            .Select(c => c.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var missing in request.RequiredCapabilities.Select(c => c.Trim().ToUpperInvariant()).Distinct().Where(c => !held.Contains(c)))
        {
            reasons.Add($"Required capability {missing} is not held.");
        }

        // 6. Vehicle availability and capacity
        var usable = fleet
            .Where(v => v.Status == RecordStatus.Active && v.AvailabilityStatus == VehicleAvailabilityStatus.Available && !blockedVehicles.Contains(v.Id))
            .ToList();
        var ofType = usable.Where(v => request.VehicleTypeId == null || v.VehicleTypeReference == request.VehicleTypeId).ToList();
        var fit = ofType
            .Where(v => v.PayloadCapacityKg >= request.RequiredWeightKg
                && (request.RequiredVolumeM3 == null || v.UsableVolumeM3 == null || v.UsableVolumeM3 >= request.RequiredVolumeM3))
            .ToList();

        if (ofType.Count == 0)
        {
            reasons.Add(request.VehicleTypeId is null ? "No vehicle is available." : "No available vehicle of the required type.");
        }
        else if (fit.Count == 0)
        {
            var volume = request.RequiredVolumeM3 is { } v3 ? $" and {v3:0.#} m³" : string.Empty;
            reasons.Add($"No available vehicle has capacity for {request.RequiredWeightKg:0.#} kg{volume} (largest available {ofType.Max(v => v.PayloadCapacityKg):0.#} kg).");
        }

        // 7. Commercial terms
        var rate = transporterRates
            .Where(r => r.Status == RecordStatus.Active
                && r.OriginLocationReference == origin
                && r.DestinationLocationReference == destination
                && r.ServiceType == service
                && (r.VehicleTypeReference == null || request.VehicleTypeId == null || r.VehicleTypeReference == request.VehicleTypeId)
                && r.EffectiveFrom <= asOfDt
                && (r.EffectiveTo == null || r.EffectiveTo >= asOfDt))
            .OrderBy(r => r.VehicleTypeReference == null ? 1 : 0)
            .ThenByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        RateQuote? quote = null;
        if (rate is null)
        {
            reasons.Add($"No applicable rate for this lane and vehicle type on {asOf:yyyy-MM-dd}.");
        }
        else
        {
            var cost = RateCalculator.EstimateCost(rate, request.RequiredWeightKg, request.RequiredVolumeM3, request.DistanceKm);
            quote = new RateQuote(rate.Id, rate.RateType, rate.RateValue, cost, rate.Currency,
                cost is null ? RateCalculator.MissingQuantityNote(rate.RateType) : null);
        }

        // 8. Performance restrictions (optional, configured)
        var kpis = Enum.GetValues<KpiType>()
            .ToDictionary(type => type, type => SelectKpi(transporterKpis, type, lane?.Id, asOf, p.PerformanceWindowDays, p.MinimumSampleSize));

        if (p.Restrictions.Enabled)
        {
            var otd = kpis[KpiType.OnTimeDelivery];
            if (otd.Sufficient && otd.Value < p.Restrictions.MinOtdPct)
            {
                reasons.Add($"On-time delivery of {otd.Value:0.0}% is below the minimum of {p.Restrictions.MinOtdPct:0.#}%.");
            }

            var claims = kpis[KpiType.ClaimsRate];
            if (claims.Sufficient && claims.Value > p.Restrictions.MaxClaimsRatePct)
            {
                reasons.Add($"Claims rate of {claims.Value:0.0}% exceeds the maximum of {p.Restrictions.MaxClaimsRatePct:0.#}%.");
            }
        }

        return new CandidateEvaluation(
            transporter.Id,
            transporter.TransporterCode,
            transporter.LegalName,
            transporter.Status,
            Eligible: reasons.Count == 0,
            Reasons: reasons,
            Warnings: warnings,
            LaneId: lane?.Id,
            Preferred: preferredCarrier || preferredLane,
            RestrictedForPlanning: activeRules.Any(r => IsExclusion(r.RuleType) && (r.LaneReference == null || r.LaneReference == lane?.Id)),
            Rate: quote,
            AvailableVehicles: fit.Count,
            ComplianceStatus: report.Overall,
            Kpis: kpis);
    }

    /// <summary>
    /// Picks the KPI to score with. A lane-level value wins when its sample is large enough; otherwise the
    /// transporter-wide value is used; otherwise the KPI is reported as insufficient, never as zero.
    /// </summary>
    public static KpiPoint SelectKpi(IEnumerable<PerformanceKpi> rows, KpiType type, long? laneId, DateOnly asOf, int windowDays, int minimumSample)
    {
        // Monthly buckets in the window are pooled: the value is the sum of numerators over the sum of denominators.
        // A lane-level value wins when its pooled sample is large enough; otherwise the transporter-wide value is used.
        var window = rows
            .Where(k => k.KpiType == type && k.VehicleTypeReference == null
                && k.PeriodEnd >= asOf.AddDays(-windowDays) && k.PeriodEnd <= asOf)
            .ToList();

        var lane = laneId is { } id ? Pool(window.Where(k => k.LaneReference == id)) : ((decimal?)null, 0m);
        if (lane.Item1 is { } laneValue && lane.Item2 >= minimumSample)
        {
            return new KpiPoint(laneValue, lane.Item2, true, "Lane");
        }

        var transporter = Pool(window.Where(k => k.LaneReference == null));
        if (transporter.Item1 is { } transporterValue && transporter.Item2 >= minimumSample)
        {
            return new KpiPoint(transporterValue, transporter.Item2, true, "Transporter");
        }

        return new KpiPoint(null, Math.Max(lane.Item2, transporter.Item2), false, "None");
    }

    private static (decimal? Value, decimal Observed) Pool(IEnumerable<PerformanceKpi> rows)
    {
        var list = rows.ToList();
        var observed = list.Sum(k => k.Denominator);
        var numerator = list.Sum(k => k.Numerator);
        return observed == 0 ? (null, 0m) : (Math.Round(numerator * 100m / observed, 2), observed);
    }

    private static PerformanceKpi? Latest(IEnumerable<PerformanceKpi> rows) =>
        rows.OrderByDescending(k => k.PeriodEnd).ThenByDescending(k => k.CalculationVersion).FirstOrDefault();

    private static bool IsExclusion(PlanningRuleType type) =>
        type is PlanningRuleType.Restricted or PlanningRuleType.DoNotAllocate;

    private static string StatusReason(TransporterStatus status) => status switch
    {
        TransporterStatus.Blacklisted => "Transporter is blacklisted.",
        TransporterStatus.Suspended => "Transporter is suspended.",
        TransporterStatus.Deactivated => "Transporter is deactivated.",
        _ => $"Transporter is {status}, not active."
    };
}
