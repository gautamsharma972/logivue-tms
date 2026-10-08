using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Reports.Domain;

/// <summary>
/// Every KPI, defined once. Reports and dashboards ask for a KPI by code; none re-derives it. Each KPI keeps its numerator and denominator so a percentage is
/// always explainable, and a KPI that cannot be judged for lack of data is "not measurable", never zero.
/// </summary>
public static class KpiCatalogue
{
    private static readonly IReadOnlyDictionary<string, string> LateDeliveries = new Dictionary<string, string> { ["status"] = "Delivered" };

    public static IReadOnlyList<KpiDefinition> All { get; } = Build();

    /// <summary>Money figures: shown only to people who may see commercial data.</summary>
    private static readonly HashSet<string> Commercial = new(StringComparer.OrdinalIgnoreCase)
    {
        "FREIGHT_SPEND", "COST_PER_SHIPMENT", "COST_PER_TON", "COST_PER_TON_KM", "CONSOLIDATION_SAVINGS", "PLANNING_SAVINGS", "LOADS_WITHOUT_RATE",
    };

    /// <summary>What a transporter may see about itself.</summary>
    private static readonly HashSet<string> VendorSafe = new(StringComparer.OrdinalIgnoreCase)
    {
        "OTP", "OTD", "TENDER_ACCEPTANCE", "TENDER_RESPONSE_RATE", "PLACEMENT_COMPLIANCE", "POD_COMPLIANCE", "CLAIMS_RATE", "SHORTAGE_PCT", "DAMAGE_PCT", "TRACKING_COVERAGE",
    };

    public static bool IsCommercial(string code) => Commercial.Contains(code);

    public static bool IsVendorSafe(string code) => VendorSafe.Contains(code);

    public static KpiDefinition? Find(string code) => All.FirstOrDefault(k => k.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public static async Task<KpiResult> CalculateAsync(KpiDefinition kpi, ReportFacts facts)
    {
        var parts = await kpi.Evaluate(facts);
        var value = KpiMath.Value(kpi.Unit, kpi.Aggregation, parts);
        var note = value is null ? $"Not measurable: {(parts.ExcludedReason ?? "there is nothing to judge in this selection")}" : parts.Excluded > 0 ? $"{parts.Excluded:N0} left out ({parts.ExcludedReason})" : null;
        return new KpiResult(kpi.Code, kpi.Name, kpi.Unit, parts.Numerator, parts.Denominator, value, value is not null, parts.Excluded, note, facts.Range.From, facts.Range.To, kpi.Version, kpi.HigherIsBetter);
    }

    private static decimal? Freight(ShipmentReportFact s) => s.IsCancelled ? null : s.FreightEstimate;

    private static IReadOnlyList<KpiDefinition> Build() =>
    [
        new()
        {
            Code = "SHIPMENTS", Name = "Total shipments", Unit = KpiUnit.Count, Aggregation = KpiAggregation.Count, Module = "Planning", SourceOfTruth = "Shipments",
            Description = "Shipments planned to be picked up in the period, cancelled ones excluded.", Formula = "Count of shipments",
            Numerator = "Shipments in the period that are not cancelled", Denominator = "The same shipments (a count has no separate denominator; zero means there are none to judge)",
            DrillReport = "R17_DELIVERY_PERFORMANCE",
            Evaluate = async f => { var n = (await f.Shipments()).Count(s => !s.IsCancelled); return new KpiParts(n, n); },
        },
        new()
        {
            Code = "FREIGHT_SPEND", Name = "Total freight spend", Unit = KpiUnit.Currency, Aggregation = KpiAggregation.Sum, Module = "Freight Contracts", SourceOfTruth = "Freight Contracts (the contract freight kept against each shipment)",
            Description = "The contractual freight kept against shipments in the period.", Formula = "Sum of contract freight", HigherIsBetter = false,
            Numerator = "Sum of the contract freight of shipments that have one", Denominator = "Number of shipments that have a contract freight",
            DrillReport = "R35_FREIGHT_RATING_AUDIT",
            Evaluate = async f =>
            {
                var priced = (await f.Shipments()).Select(Freight).Where(v => v is not null).Select(v => v!.Value).ToList();
                var unpriced = (await f.Shipments()).Count(s => !s.IsCancelled && s.FreightEstimate is null);
                return new KpiParts(priced.Sum(), priced.Count, unpriced, "no contract freight kept yet");
            },
        },
        new()
        {
            Code = "COST_PER_SHIPMENT", Name = "Average cost per shipment", Unit = KpiUnit.Currency, Aggregation = KpiAggregation.Average, Module = "Freight Contracts", SourceOfTruth = "Freight Contracts",
            Description = "Contract freight divided by the shipments that have one.", Formula = "Total freight ÷ shipments with a freight", HigherIsBetter = false,
            Numerator = "Sum of contract freight", Denominator = "Shipments with a contract freight", DrillReport = "R37_LANE_PERFORMANCE",
            Evaluate = async f =>
            {
                var priced = (await f.Shipments()).Select(Freight).Where(v => v is not null).Select(v => v!.Value).ToList();
                return new KpiParts(priced.Sum(), priced.Count);
            },
        },
        new()
        {
            Code = "COST_PER_TON", Name = "Cost per ton", Unit = KpiUnit.Currency, Aggregation = KpiAggregation.Average, Module = "Freight Contracts", SourceOfTruth = "Freight Contracts and Shipments (weight)",
            Description = "Contract freight divided by the tons moved on the shipments that have a freight.", Formula = "Total freight ÷ total tons", HigherIsBetter = false,
            Numerator = "Sum of contract freight", Denominator = "Sum of tons (weight ÷ 1000) of the same shipments", DrillReport = "R37_LANE_PERFORMANCE",
            Evaluate = async f =>
            {
                var rows = (await f.Shipments()).Where(s => Freight(s) is not null && s.WeightKg > 0).ToList();
                return new KpiParts(rows.Sum(s => Freight(s)!.Value), rows.Sum(s => s.WeightKg) / 1000m);
            },
        },
        new()
        {
            Code = "COST_PER_TON_KM", Name = "Cost per ton-km", Unit = KpiUnit.Currency, Aggregation = KpiAggregation.Average, Module = "Freight Contracts", SourceOfTruth = "Freight Contracts, Shipments (weight and distance)",
            Description = "Contract freight divided by the ton-kilometres of the shipments that have a freight and a distance.", Formula = "Total freight ÷ Σ(tons × km)", HigherIsBetter = false,
            Numerator = "Sum of contract freight", Denominator = "Σ (tons × distance km) of the same shipments", DrillReport = "R37_LANE_PERFORMANCE",
            Evaluate = async f =>
            {
                var all = (await f.Shipments()).Where(s => Freight(s) is not null && s.WeightKg > 0).ToList();
                var rows = all.Where(s => s.DistanceKm is > 0).ToList();
                return new KpiParts(rows.Sum(s => Freight(s)!.Value), rows.Sum(s => s.WeightKg / 1000m * s.DistanceKm!.Value), all.Count - rows.Count, "no distance recorded");
            },
        },
        Mix("FTL_PCT", "FTL share", "FTL"),
        Mix("PTL_PCT", "PTL share", "PTL"),
        Mix("DEDICATED_PCT", "Dedicated share", "Dedicated"),
        new()
        {
            Code = "OTP", Name = "On-time pickup (OTP)", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Transporters", SourceOfTruth = "Shipments (planned and actual pickup)",
            Description = "Share of pickups made at or before the planned time (plus any grace the organisation allows).", Formula = "On-time pickups ÷ pickups with a planned and an actual time × 100",
            Numerator = "Pickups where actual ≤ planned + grace", Denominator = "Shipments that have both a planned and an actual pickup time", DrillReport = "R14_OTP_OTD",
            Evaluate = async f =>
            {
                var all = (await f.Shipments()).Where(s => !s.IsCancelled && s.ActualPickupAt is not null).ToList();
                var judged = all.Where(s => s.PlannedPickupAt is not null).ToList();
                var on = judged.Count(s => s.ActualPickupAt!.Value <= s.PlannedPickupAt!.Value.AddMinutes(f.Settings.OtpGraceMinutes));
                return new KpiParts(on, judged.Count, all.Count - judged.Count, "no planned pickup time");
            },
        },
        new()
        {
            Code = "OTD", Name = "On-time delivery (OTD)", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Deliveries", SourceOfTruth = "POD & Delivery (its own on-time judgement)",
            Description = "Share of completed deliveries made by their promised time.", Formula = "On-time deliveries ÷ deliveries that could be judged × 100",
            Numerator = "Eligible deliveries completed on or before the promised time", Denominator = "Eligible deliveries with a valid planned delivery time",
            DrillReport = "R17_DELIVERY_PERFORMANCE", DrillFilters = LateDeliveries,
            Evaluate = async f =>
            {
                var done = (await f.Deliveries()).Where(d => d.Status is "Delivered" or "PartiallyDelivered").ToList();
                var judged = done.Where(d => d.OnTime is not null).ToList();
                return new KpiParts(judged.Count(d => d.OnTime == true), judged.Count, done.Count - judged.Count, "no planned delivery time");
            },
        },
        new()
        {
            Code = "TRANSPORTER_SCORE", Name = "Transporter score", Unit = KpiUnit.Number, Aggregation = KpiAggregation.Average, Module = "Transporters", SourceOfTruth = "Transporter Management (scorecards)",
            Description = "Average overall score the Transporters module gave carriers for the period. Shown, never recalculated here.", Formula = "Average of overall scores",
            Numerator = "Sum of overall scores", Denominator = "Scorecards with a score", DrillReport = "R09_TRANSPORTER_SCORECARD",
            Evaluate = async f =>
            {
                var rows = (await f.Scorecards()).Where(s => s.OverallScore is not null).ToList();
                return new KpiParts(rows.Sum(s => s.OverallScore!.Value), rows.Count);
            },
        },
        new()
        {
            Code = "TENDER_ACCEPTANCE", Name = "Tender acceptance", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Transporters", SourceOfTruth = "Shipments (tenders)",
            Description = "Share of answered or expired tenders that the transporter accepted. Withdrawn offers and ones still open are left out.", Formula = "Accepted ÷ (accepted + rejected + expired) × 100",
            Numerator = "Tenders accepted", Denominator = "Tenders accepted, rejected or expired", DrillReport = "R12_TENDER_PERFORMANCE",
            Evaluate = async f =>
            {
                var t = await f.Tenders();
                var counted = t.Where(x => x.Outcome is "Accepted" or "Rejected" or "Expired").ToList();
                return new KpiParts(counted.Count(x => x.Outcome == "Accepted"), counted.Count, t.Count - counted.Count, "still open or withdrawn");
            },
        },
        new()
        {
            Code = "TENDER_RESPONSE_RATE", Name = "Tender response rate", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Transporters", SourceOfTruth = "Shipments (tenders)",
            Description = "Share of offered tenders that were answered (accepted or rejected) before they expired.", Formula = "Answered ÷ offered × 100",
            Numerator = "Tenders accepted or rejected", Denominator = "Tenders offered and no longer open", DrillReport = "R12_TENDER_PERFORMANCE",
            Evaluate = async f =>
            {
                var t = (await f.Tenders()).Where(x => x.Outcome != "Pending" && x.Outcome != "Withdrawn").ToList();
                return new KpiParts(t.Count(x => x.Outcome is "Accepted" or "Rejected"), t.Count);
            },
        },
        new()
        {
            Code = "WEIGHT_UTIL", Name = "Weight utilisation", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Planning", SourceOfTruth = "Planning (vehicle load and capacity)",
            Description = "Weight carried as a share of the payload of the vehicles used.", Formula = "Σ weight ÷ Σ payload × 100",
            Numerator = "Σ planned weight (kg)", Denominator = "Σ payload of the same vehicles (kg)", DrillReport = "R04_VEHICLE_UTILISATION",
            Evaluate = async f =>
            {
                var all = await f.Vehicles();
                var rows = all.Where(v => v.CapacityKg is > 0).ToList();
                return new KpiParts(rows.Sum(v => v.WeightKg), rows.Sum(v => (decimal)v.CapacityKg!.Value), all.Count - rows.Count, "vehicle payload unknown");
            },
        },
        new()
        {
            Code = "VOLUME_UTIL", Name = "Volume utilisation", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Planning", SourceOfTruth = "Planning (vehicle load and capacity)",
            Description = "Volume carried as a share of the cubic capacity of the vehicles used.", Formula = "Σ volume ÷ Σ capacity × 100",
            Numerator = "Σ planned volume (CBM)", Denominator = "Σ cubic capacity of the same vehicles (CBM)", DrillReport = "R04_VEHICLE_UTILISATION",
            Evaluate = async f =>
            {
                var all = await f.Vehicles();
                var rows = all.Where(v => v.CapacityCbm is > 0).ToList();
                return new KpiParts(rows.Sum(v => v.VolumeCbm), rows.Sum(v => v.CapacityCbm!.Value), all.Count - rows.Count, "vehicle cubic capacity unknown");
            },
        },
        new()
        {
            Code = "POD_COMPLIANCE", Name = "POD compliance", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Deliveries", SourceOfTruth = "POD & Delivery (submission within its SLA)",
            Description = "Share of required proofs of delivery submitted within the SLA. Deliveries that need no proof are left out (not applicable), never counted as zero.", Formula = "Submitted within SLA ÷ proofs required (and judgeable) × 100",
            Numerator = "Required proofs submitted within SLA", Denominator = "Required proofs whose timeliness can be judged", DrillReport = "R18_POD_COMPLIANCE",
            Evaluate = async f =>
            {
                var required = (await f.Pods()).Where(p => p.Required).ToList();
                var judged = required.Where(p => p.SubmittedWithinSla is not null).ToList();
                return new KpiParts(judged.Count(p => p.SubmittedWithinSla == true), judged.Count, required.Count - judged.Count, "not yet due");
            },
        },
        new()
        {
            Code = "PLACEMENT_COMPLIANCE", Name = "Placement compliance", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Transporters", SourceOfTruth = "Transporter Management (vehicle placements)",
            Description = "Share of vehicle placements that arrived on time.", Formula = "On-time placements ÷ placements × 100",
            Numerator = "Placements marked on time", Denominator = "Placements (late, no-show and replaced ones count against)", DrillReport = "R13_PLACEMENT_COMPLIANCE",
            Evaluate = async f =>
            {
                var p = await f.Placements();
                return new KpiParts(p.Count(x => x.Outcome == "OnTime"), p.Count);
            },
        },
        new()
        {
            Code = "CLAIMS_RATE", Name = "Claims rate", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Deliveries", SourceOfTruth = "POD & Delivery (discrepancies handed to claims)",
            Description = "Share of completed deliveries that led to a claim.", Formula = "Deliveries with a claim ÷ completed deliveries × 100", HigherIsBetter = false,
            Numerator = "Distinct deliveries with a shortage or damage that has a claim reference", Denominator = "Deliveries completed (fully or partly)", DrillReport = "R21_SHORTAGE",
            Evaluate = async f =>
            {
                var done = (await f.Deliveries()).Count(d => d.Status is "Delivered" or "PartiallyDelivered");
                var claims = (await f.Discrepancies()).Where(d => d.ClaimRef is not null).Select(d => d.DeliveryRef).Distinct().Count();
                return new KpiParts(claims, done);
            },
        },
        Discrepancy("SHORTAGE_PCT", "Shortage rate", "Shortage", "R21_SHORTAGE"),
        Discrepancy("DAMAGE_PCT", "Damage rate", "Damage", "R22_DAMAGE"),
        new()
        {
            Code = "TRACKING_COVERAGE", Name = "Tracking coverage", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Tracking", SourceOfTruth = "Tracking",
            Description = "Share of trips that have been on the road (or are) for which tracking ever started.", Formula = "Trips with tracking started ÷ trips that departed × 100",
            Numerator = "Departed trips whose tracking health is not 'Not started'", Denominator = "Trips in transit or completed", DrillReport = "R28_TRACKING_HEALTH",
            Evaluate = async f =>
            {
                var t = (await f.Trips()).Where(x => x.Execution is "InTransit" or "Completed").ToList();
                return new KpiParts(t.Count(x => x.Health != "NotStarted"), t.Count);
            },
        },
        new()
        {
            Code = "TRACKING_LOST", Name = "Tracking lost", Unit = KpiUnit.Count, Aggregation = KpiAggregation.Count, Module = "Tracking", SourceOfTruth = "Tracking", HigherIsBetter = false,
            Description = "Trips whose phone has stopped reporting for longer than the lost threshold.", Formula = "Count of trips with health 'Lost'",
            Numerator = "Trips with tracking Lost", Denominator = "Trips in the selection", DrillReport = "R28_TRACKING_HEALTH", DrillFilters = new Dictionary<string, string> { ["status"] = "Lost" },
            Evaluate = async f => { var t = await f.Trips(); return new KpiParts(t.Count(x => x.Health == "Lost"), t.Count); },
        },
        new()
        {
            Code = "ROUTE_DEVIATIONS", Name = "Route deviations", Unit = KpiUnit.Count, Aggregation = KpiAggregation.Count, Module = "Tracking", SourceOfTruth = "Tracking", HigherIsBetter = false,
            Description = "Times a vehicle left its planned route beyond the allowed distance.", Formula = "Count of deviations",
            Numerator = "Deviations detected in the period", Denominator = "Trips in the selection", DrillReport = "R26_ROUTE_DEVIATION",
            Evaluate = async f => { var d = await f.Deviations(); var t = await f.Trips(); return new KpiParts(d.Count, t.Count); },
        },
        new()
        {
            Code = "ETA_ACCURACY", Name = "ETA accuracy", Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Tracking", SourceOfTruth = "Tracking (ETA predictions)",
            Description = "Share of completed trips whose last ETA was within the tolerance of the actual arrival.", Formula = "Trips with |actual − last ETA| ≤ tolerance ÷ completed trips with an ETA × 100",
            Numerator = "Completed trips with an ETA error within tolerance", Denominator = "Completed trips that had an ETA and an actual arrival", DrillReport = "R25_ETA_DELAY",
            Evaluate = async f =>
            {
                var done = (await f.Trips()).Where(t => t.ActualArrival is not null).ToList();
                var judged = done.Where(t => t.LatestEta is not null).ToList();
                return new KpiParts(judged.Count(t => Math.Abs((t.ActualArrival!.Value - t.LatestEta!.Value).TotalMinutes) <= f.Settings.EtaToleranceMinutes), judged.Count, done.Count - judged.Count, "no ETA was made");
            },
        },
        new()
        {
            Code = "ETA_ERROR_MIN", Name = "Average ETA error", Unit = KpiUnit.Minutes, Aggregation = KpiAggregation.Average, Module = "Tracking", SourceOfTruth = "Tracking (ETA predictions)", HigherIsBetter = false,
            Description = "Average distance in minutes between the last ETA and the actual arrival.", Formula = "Σ |actual − last ETA| ÷ completed trips with an ETA",
            Numerator = "Sum of absolute ETA errors (minutes)", Denominator = "Completed trips with an ETA", DrillReport = "R25_ETA_DELAY",
            Evaluate = async f =>
            {
                var judged = (await f.Trips()).Where(t => t.ActualArrival is not null && t.LatestEta is not null).ToList();
                return new KpiParts((decimal)judged.Sum(t => Math.Abs((t.ActualArrival!.Value - t.LatestEta!.Value).TotalMinutes)), judged.Count);
            },
        },
        new()
        {
            Code = "CONSOLIDATION_SAVINGS", Name = "Consolidation savings", Unit = KpiUnit.Currency, Aggregation = KpiAggregation.Sum, Module = "Planning", SourceOfTruth = "Planning (cost if shipped separately)",
            Description = "What putting orders on shared trips saved compared with sending them separately.", Formula = "Σ (cost if separate − cost of the consolidated trip)",
            Numerator = "Σ savings of consolidated trips", Denominator = "Consolidated trips with a separate-cost estimate", DrillReport = "R07_CONSOLIDATION_SAVINGS",
            Evaluate = async f =>
            {
                var rows = (await f.Vehicles()).Where(v => v.Consolidated && v.CostIfSeparate is not null).ToList();
                return new KpiParts(rows.Sum(v => v.CostIfSeparate!.Value - v.EstimatedCost), rows.Count);
            },
        },
        new()
        {
            Code = "PLANNING_SAVINGS", Name = "Planning savings", Unit = KpiUnit.Currency, Aggregation = KpiAggregation.Sum, Module = "Planning", SourceOfTruth = "Planning (run savings)",
            Description = "What the planner's optimisation saved against its own baseline, as calculated by each planning run.", Formula = "Σ planning run savings",
            Numerator = "Σ savings of runs that report one", Denominator = "Runs that report a saving", DrillReport = "R02_LOAD_PLANNING_SUMMARY",
            Evaluate = async f =>
            {
                var rows = (await f.Runs()).Where(r => r.PlanningSavings is not null).ToList();
                return new KpiParts(rows.Sum(r => r.PlanningSavings!.Value), rows.Count);
            },
        },
        new()
        {
            Code = "OPEN_CRITICAL_EXCEPTIONS", Name = "Open critical exceptions", Unit = KpiUnit.Count, Aggregation = KpiAggregation.Count, Module = "Tracking", SourceOfTruth = "Deliveries, Tracking and Transporters (their own exceptions)", HigherIsBetter = false,
            Description = "Exceptions of Critical severity that nobody has resolved yet, from every module.", Formula = "Count of unresolved Critical exceptions",
            Numerator = "Critical exceptions not resolved", Denominator = "Exceptions in the selection", DrillReport = "R16_TRANSPORTER_EXCEPTIONS", DrillFilters = new Dictionary<string, string> { ["severity"] = "Critical" },
            Evaluate = async f => { var e = await f.Exceptions(); return new KpiParts(e.Count(x => x.Severity == "Critical" && x.Status != "Resolved"), e.Count); },
        },
        new()
        {
            Code = "LOADS_WITHOUT_RATE", Name = "Loads without an applicable rate", Unit = KpiUnit.Count, Aggregation = KpiAggregation.Count, Module = "Freight Contracts", SourceOfTruth = "Freight Contracts (rate coverage)", HigherIsBetter = false,
            Description = "Loads on lanes where no contract rate applies.", Formula = "Σ loads without a rate",
            Numerator = "Loads with no applicable rate", Denominator = "Loads on covered or uncovered lanes", DrillReport = "R32_RATE_COVERAGE",
            Evaluate = async f => { var c = await f.Coverage(); return new KpiParts(c.Sum(x => x.LoadsWithoutRate), c.Sum(x => x.Loads)); },
        },
    ];

    private static KpiDefinition Mix(string code, string name, string service) => new()
    {
        Code = code, Name = name, Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Planning", SourceOfTruth = "Shipments",
        Description = $"{service} shipments as a share of all shipments.", Formula = $"{service} shipments ÷ shipments × 100",
        Numerator = $"{service} shipments", Denominator = "All shipments in the period (cancelled excluded)", DrillReport = "R06_FTL_VS_PTL",
        Evaluate = async f =>
        {
            var s = (await f.Shipments()).Where(x => !x.IsCancelled).ToList();
            return new KpiParts(s.Count(x => x.Service.Equals(service, StringComparison.OrdinalIgnoreCase)), s.Count);
        },
    };

    private static KpiDefinition Discrepancy(string code, string name, string type, string drill) => new()
    {
        Code = code, Name = name, Unit = KpiUnit.Percent, Aggregation = KpiAggregation.Percent, Module = "Deliveries", SourceOfTruth = "POD & Delivery", HigherIsBetter = false,
        Description = $"Share of completed deliveries with a {type.ToLowerInvariant()} recorded.", Formula = $"Deliveries with a {type.ToLowerInvariant()} ÷ completed deliveries × 100",
        Numerator = $"Distinct deliveries with a {type.ToLowerInvariant()}", Denominator = "Deliveries completed (fully or partly)", DrillReport = drill,
        Evaluate = async f =>
        {
            var done = (await f.Deliveries()).Count(d => d.Status is "Delivered" or "PartiallyDelivered");
            var hit = (await f.Discrepancies()).Where(d => d.Type == type).Select(d => d.DeliveryRef).Distinct().Count();
            return new KpiParts(hit, done);
        },
    };
}
