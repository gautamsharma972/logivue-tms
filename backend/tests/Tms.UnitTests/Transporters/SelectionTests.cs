using Tms.Modules.Transporters.Application.Selection;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Transporters;

public class SelectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 6);
    private static readonly Guid Truck = Guid.NewGuid();

    private static SelectionRequest Request(bool urgent = false, IReadOnlyList<string>? capabilities = null, decimal weight = 5_000m) =>
        new("Maharashtra", "Pune", "Gujarat", "Surat", FreightMode.Ftl, Truck, weight, null, Day, capabilities, urgent);

    private static Transporter Carrier(string name, TransporterStatus status = TransporterStatus.Active)
    {
        var t = (Transporter)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Transporter));
        typeof(Transporter).GetProperty(nameof(Transporter.Id))!.SetValue(t, Guid.NewGuid());
        typeof(Transporter).GetProperty(nameof(Transporter.LegalName))!.SetValue(t, name);
        typeof(Transporter).GetProperty(nameof(Transporter.Code))!.SetValue(t, "TR-" + name[..2].ToUpperInvariant());
        typeof(Transporter).GetProperty(nameof(Transporter.Status))!.SetValue(t, status);
        return t;
    }

    private static FleetVehicle Vehicle(Guid transporterId, int payload = 16_000, FleetCompliance compliance = FleetCompliance.Compliant, FleetAvailability availability = FleetAvailability.Available) =>
        new(Guid.NewGuid(), transporterId, "MH12AB" + Random.Shared.Next(1000, 9999), Truck, "32 ft", payload, true, compliance, [], availability);

    private static TransporterLane Lane(Guid transporterId, int? sla = null) =>
        TransporterLane.Create(Tenant, transporterId, "Maharashtra", "Pune", "Gujarat", null, null, sla, new DateOnly(2026, 1, 1), null).Value;

    private static CandidateData Data(
        Transporter t, IReadOnlyList<TransporterLane>? lanes = null, IReadOnlyList<PlanningRule>? rules = null, IReadOnlyList<FleetVehicle>? vehicles = null,
        IReadOnlyList<TransporterCapability>? capabilities = null, RateQuote? rate = null, IReadOnlyList<DocumentIssue>? issues = null, IReadOnlyList<PerformanceKpi>? kpis = null) =>
        new(t, lanes ?? [Lane(t.Id)], capabilities ?? [], rules ?? [], issues ?? [], vehicles ?? [Vehicle(t.Id)], kpis ?? [], rate ?? new RateQuote(Guid.NewGuid(), "CN-1", 30_000m, null));

    private static readonly EligibilityParameters Parameters = new(90, 20, new EligibilityRestrictionsSetting(false, 90, 3));

    private static CandidateEvaluation One(CandidateData data, SelectionRequest? request = null, EligibilityParameters? p = null) =>
        EligibilityEvaluator.Evaluate(request ?? Request(), [data], p ?? Parameters).Single();

    [Fact]
    public void A_carrier_with_a_lane_a_rate_and_a_free_vehicle_is_eligible()
    {
        var result = One(Data(Carrier("Alpha")));

        result.Eligible.ShouldBeTrue();
        result.Reasons.ShouldBeEmpty();
        result.AvailableVehicles.ShouldBe(1);
    }

    [Fact]
    public void Every_exclusion_is_explained()
    {
        var t = Carrier("Beta", TransporterStatus.Suspended);

        var result = One(Data(t, lanes: [], vehicles: []) with { Rate = null });
        result.Eligible.ShouldBeFalse();
        result.Reasons.ShouldContain(r => r.Contains("suspended"));
        result.Reasons.ShouldContain(r => r.StartsWith("Lane not configured"));
        result.Reasons.ShouldContain(r => r.Contains("No available vehicle"));
        result.Reasons.ShouldContain(r => r.Contains("No contract rate"));
    }

    [Fact]
    public void Missing_capabilities_vehicles_that_are_too_small_or_not_in_order_or_in_the_workshop_exclude()
    {
        var t = Carrier("Gamma");

        One(Data(t), Request(capabilities: [CapabilityCatalog.Hazardous])).Reasons.ShouldContain(r => r.Contains("HAZARDOUS"));
        One(Data(t, vehicles: [Vehicle(t.Id, payload: 3_000)])).Reasons.ShouldContain(r => r.Contains("capacity for 5000"));
        One(Data(t, vehicles: [Vehicle(t.Id, compliance: FleetCompliance.NonCompliant)])).Reasons.ShouldContain(r => r.Contains("No available vehicle"));
        One(Data(t, vehicles: [Vehicle(t.Id, availability: FleetAvailability.InMaintenance)])).Eligible.ShouldBeFalse();

        var held = TransporterCapability.Create(Tenant, t.Id, "hazardous", new DateOnly(2026, 1, 1), null).Value;
        One(Data(t, capabilities: [held]), Request(capabilities: [CapabilityCatalog.Hazardous])).Eligible.ShouldBeTrue();
    }

    [Fact]
    public void A_lanes_committed_transit_must_fit_the_time_between_pickup_and_delivery()
    {
        var t = Carrier("Delta");
        var request = Request() with { PickupBy = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero), DeliverBy = new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.Zero) };

        One(Data(t, lanes: [Lane(t.Id, sla: 600)]), request).Reasons.ShouldContain(r => r.Contains("Committed transit of 10 h"));
        One(Data(t, lanes: [Lane(t.Id, sla: 240)]), request).Eligible.ShouldBeTrue();
    }

    [Fact]
    public void Planning_rules_bar_prefer_and_avoid_for_urgent_loads()
    {
        var t = Carrier("Epsilon");
        PlanningRule Rule(PlanningRuleType type, Guid? lane = null) => PlanningRule.Create(Tenant, t.Id, type, lane, "Because", new DateOnly(2026, 1, 1), null).Value;

        One(Data(t, rules: [Rule(PlanningRuleType.DoNotAllocate)])).Reasons.ShouldContain(r => r.Contains("DoNotAllocate"));
        One(Data(t, rules: [Rule(PlanningRuleType.Restricted)])).RestrictedForPlanning.ShouldBeTrue();
        One(Data(t, rules: [Rule(PlanningRuleType.AvoidForUrgent)]), Request(urgent: true)).Reasons.ShouldContain("Avoided for urgent loads by a planning rule.");
        One(Data(t, rules: [Rule(PlanningRuleType.AvoidForUrgent)]), Request(urgent: false)).Eligible.ShouldBeTrue();
        One(Data(t, rules: [Rule(PlanningRuleType.PreferredCarrier)])).Preferred.ShouldBeTrue();

        // A rule that has ended no longer applies; one for another lane does not apply here.
        var ended = Rule(PlanningRuleType.DoNotAllocate);
        ended.End("Resolved", new DateOnly(2026, 9, 1)).IsSuccess.ShouldBeTrue();
        One(Data(t, rules: [ended])).Eligible.ShouldBeTrue();
        One(Data(t, rules: [Rule(PlanningRuleType.Restricted, Guid.NewGuid())])).Eligible.ShouldBeTrue();
    }

    [Fact]
    public void Expired_papers_bar_and_papers_expiring_soon_only_warn()
    {
        var t = Carrier("Zeta");

        One(Data(t, issues: [new DocumentIssue("GstCertificate expired on 01 Sep 2026.", true)])).Reasons.ShouldContain(r => r.StartsWith("Compliance:"));
        var warned = One(Data(t, issues: [new DocumentIssue("Insurance expires on 20 Oct 2026.", false)]));
        warned.Eligible.ShouldBeTrue();
        warned.Warnings.ShouldContain(w => w.Contains("Insurance"));
    }

    private static PerformanceKpi Kpi(Guid transporterId, KpiType type, decimal n, decimal d, Guid? lane = null) =>
        PerformanceKpi.Create(Tenant, transporterId, lane, null, type, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), n, d, d == 0 ? null : n * 100 / d, 1);

    [Fact]
    public void Performance_limits_apply_only_when_switched_on_and_only_to_kpis_with_enough_loads()
    {
        var t = Carrier("Eta");
        var weakOtd = new[] { Kpi(t.Id, KpiType.OnTimeDelivery, 10, 40) }; // 25%
        var limits = Parameters with { Restrictions = new EligibilityRestrictionsSetting(true, 90, 3) };

        One(Data(t, kpis: weakOtd), p: Parameters).Eligible.ShouldBeTrue(); // off by default
        One(Data(t, kpis: weakOtd), p: limits).Reasons.ShouldContain(r => r.Contains("On-time delivery of 25.0%"));
        One(Data(t, kpis: [Kpi(t.Id, KpiType.OnTimeDelivery, 1, 4)]), p: limits).Eligible.ShouldBeTrue(); // 4 loads prove nothing
    }

    [Fact]
    public void A_lane_level_kpi_wins_when_its_sample_is_large_enough_otherwise_the_overall_one_is_used()
    {
        var lane = Guid.NewGuid();
        var rows = new[] { Kpi(Guid.Empty, KpiType.OnTimeDelivery, 90, 100), Kpi(Guid.Empty, KpiType.OnTimeDelivery, 5, 25, lane) };

        var onLane = EligibilityEvaluator.SelectKpi(rows, KpiType.OnTimeDelivery, lane, new DateOnly(2026, 10, 6), 90, 20);
        var smallLane = EligibilityEvaluator.SelectKpi([rows[0], Kpi(Guid.Empty, KpiType.OnTimeDelivery, 1, 5, lane)], KpiType.OnTimeDelivery, lane, new DateOnly(2026, 10, 6), 90, 20);

        (onLane.Value, onLane.Scope).ShouldBe((20m, "Lane"));
        (smallLane.Value, smallLane.Scope).ShouldBe((90m, "Transporter"));
        EligibilityEvaluator.SelectKpi([], KpiType.OnTimeDelivery, null, new DateOnly(2026, 10, 6), 90, 20).Sufficient.ShouldBeFalse();
    }

    private static CandidateEvaluation Candidate(string name, decimal rate, bool preferred = false, int vehicles = 3, params PerformanceKpi[] kpis)
    {
        var t = Carrier(name);
        var evaluation = One(Data(t, rate: new RateQuote(Guid.NewGuid(), "CN", rate, null), kpis: kpis, vehicles: Enumerable.Range(0, vehicles).Select(_ => Vehicle(t.Id)).ToList(),
            rules: preferred ? [PlanningRule.Create(Tenant, t.Id, PlanningRuleType.PreferredCarrier, null, "Strategic partner", new DateOnly(2026, 1, 1), null).Value] : []));
        evaluation.Eligible.ShouldBeTrue(string.Join("; ", evaluation.Reasons));
        return evaluation;
    }

    private static IReadOnlyList<RankedCandidate> Rank(params CandidateEvaluation[] candidates) =>
        RecommendationScorer.Rank(candidates, (RecommendationWeightsSetting)SettingDefaults.For(SettingKeys.RecommendationWeights)!, (RecommendationScoringSetting)SettingDefaults.For(SettingKeys.RecommendationScoring)!, 20);

    [Fact]
    public void The_cheapest_carrier_is_not_automatically_recommended()
    {
        var cheap = Candidate("Cheap", 24_000m); // no history at all
        var solid = Candidate("Solid", 27_000m, kpis: [Kpi(Guid.Empty, KpiType.OnTimeDelivery, 98, 100), Kpi(Guid.Empty, KpiType.OnTimePickup, 97, 100)]);
        var weak = Candidate("Weak", 22_000m, kpis: [Kpi(Guid.Empty, KpiType.OnTimeDelivery, 40, 100), Kpi(Guid.Empty, KpiType.OnTimePickup, 45, 100)]);

        var ranked = Rank(cheap, solid, weak);

        ranked[0].Candidate.Name.ShouldBe("Solid");
        ranked[2].Candidate.Name.ShouldBe("Weak"); // the cheapest of all, but the worst record
        ranked[1].Explanations.ShouldContain(e => e.Contains("scored neutrally")); // no data is neither a reward nor a penalty
        ranked[2].Comparisons.ShouldContain(c => c.Contains("lower rate"));
    }

    [Fact]
    public void A_preferred_carrier_gets_its_bonus_and_missing_data_scores_neutrally_not_zero()
    {
        var plain = Candidate("Plain", 30_000m);
        var preferred = Candidate("Fav", 30_000m, preferred: true);

        var ranked = Rank(plain, preferred);

        ranked[0].Candidate.Name.ShouldBe("Fav");
        (ranked[0].RecommendationScore - ranked[1].RecommendationScore).ShouldBe(5m);
        ranked[1].Components.Where(c => !c.Sufficient).ShouldAllBe(c => c.Score == 60m);
    }

    [Fact]
    public void Nothing_to_rank_gives_nothing()
    {
        Rank().ShouldBeEmpty();
    }

    [Fact]
    public void Capabilities_and_planning_rules_validate_their_input()
    {
        TransporterCapability.Create(Tenant, Guid.NewGuid(), "TELEPORTATION", new DateOnly(2026, 1, 1), null).Error.Code.ShouldBe("capabilities.unknown");
        PlanningRule.Create(Tenant, Guid.NewGuid(), PlanningRuleType.Restricted, null, " ", new DateOnly(2026, 1, 1), null).Error.ValidationErrors!.ShouldContainKey("reason");
        PlanningRule.Create(Tenant, Guid.NewGuid(), PlanningRuleType.PreferredLane, null, "x", new DateOnly(2026, 1, 1), null).Error.ValidationErrors!.ShouldContainKey("laneId");
    }
}
