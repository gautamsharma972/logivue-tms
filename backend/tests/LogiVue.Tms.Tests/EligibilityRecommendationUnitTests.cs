using FluentAssertions;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Recommendation;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Pure rules for pricing, KPI sample selection, scoring and eligibility reasons.</summary>
public class EligibilityRecommendationUnitTests
{
    private static readonly DateOnly RequestDate = new(2026, 10, 10);

    // ---------- rate calculation ----------

    [Fact]
    public void Per_kg_rate_multiplies_weight_and_adds_surcharges()
    {
        var rate = new TransporterRate { RateType = RateType.PerKg, RateValue = 2m, FuelSurcharge = 500m, TollAmount = 250m };

        RateCalculator.EstimateCost(rate, 12000m, null, null).Should().Be(24750m);
    }

    [Fact]
    public void Minimum_charge_applies_when_the_basis_is_lower()
    {
        var rate = new TransporterRate { RateType = RateType.PerTrip, RateValue = 1000m, MinimumCharge = 5000m };

        RateCalculator.EstimateCost(rate, 100m, null, null).Should().Be(5000m);
    }

    [Fact]
    public void Per_km_rate_without_distance_is_not_priced()
    {
        var rate = new TransporterRate { RateType = RateType.PerKm, RateValue = 40m };

        RateCalculator.EstimateCost(rate, 12000m, null, null).Should().BeNull();
        RateCalculator.MissingQuantityNote(RateType.PerKm).Should().Contain("Distance");
    }

    // ---------- KPI sample selection ----------

    [Fact]
    public void Sufficient_lane_kpi_overrides_the_transporter_average()
    {
        var rows = new[]
        {
            Kpi(KpiType.OnTimeDelivery, 90m, 500, lane: null),
            Kpi(KpiType.OnTimeDelivery, 97m, 120, lane: 7)
        };

        var point = TransporterEligibilityEvaluator.SelectKpi(rows, KpiType.OnTimeDelivery, 7, RequestDate, 90, 20);

        point.Sufficient.Should().BeTrue();
        point.Scope.Should().Be("Lane");
        point.Value.Should().Be(97m);
    }

    [Fact]
    public void Small_lane_sample_falls_back_to_the_transporter_value()
    {
        var rows = new[]
        {
            Kpi(KpiType.OnTimeDelivery, 88m, 300, lane: null),
            Kpi(KpiType.OnTimeDelivery, 100m, 4, lane: 7)
        };

        var point = TransporterEligibilityEvaluator.SelectKpi(rows, KpiType.OnTimeDelivery, 7, RequestDate, 90, 20);

        point.Scope.Should().Be("Transporter");
        point.Value.Should().Be(88m);
    }

    [Fact]
    public void Missing_data_is_reported_as_insufficient_never_as_zero()
    {
        var point = TransporterEligibilityEvaluator.SelectKpi([], KpiType.OnTimeDelivery, 7, RequestDate, 90, 20);

        point.Sufficient.Should().BeFalse();
        point.Value.Should().BeNull();
        point.Denominator.Should().Be(0);
    }

    // ---------- recommendation ----------

    [Fact]
    public void Cheapest_carrier_is_not_recommended_when_its_service_is_poor()
    {
        var ranked = RecommendationScorer.Rank(
            [
                Candidate("ABC", "ABC Logistics", 42000m, preferred: true, otd: 96.5m, otp: 96.5m, placement: 94.8m, pod: 98.1m, tender: 97.2m, claims: 0.6m),
                Candidate("XYZ", "XYZ Transport", 39000m, preferred: false, otd: 81.0m, otp: 81.0m, placement: 84m, pod: 91m, tender: 85m, claims: 2.8m),
                Candidate("PQR", "PQR Roadways", 41000m, preferred: false, otd: 92m, otp: 92m, placement: 89m, pod: 95m, tender: 90m, claims: 1.4m),
            ],
            Weights, Scoring, minimumSample: 20);

        ranked.Should().HaveCount(3);
        ranked[0].Candidate.Code.Should().Be("ABC");
        ranked[1].Candidate.Code.Should().Be("PQR");
        ranked[2].Candidate.Code.Should().Be("XYZ", "it is the cheapest, but its performance ranks it last");
    }

    [Fact]
    public void Comparison_explains_the_cheaper_carrier_in_terms_of_rate_and_delivery()
    {
        var ranked = RecommendationScorer.Rank(
            [
                Candidate("ABC", "ABC Logistics", 42000m, preferred: true, otd: 96.4m, otp: 96.4m, placement: 94.8m, pod: 98.1m, tender: 97.2m, claims: 0.6m),
                Candidate("XYZ", "XYZ Transport", 39000m, preferred: false, otd: 81.2m, otp: 81.2m, placement: 84m, pod: 91m, tender: 85m, claims: 2.8m),
            ],
            Weights, Scoring, minimumSample: 20);

        var xyz = ranked.Single(r => r.Candidate.Code == "XYZ");
        xyz.Comparisons.Should().Contain(c => c.Contains("lower rate by ₹3,000"));
        xyz.Comparisons.Should().Contain(c => c.Contains("On-time delivery") && c.Contains("81.2"));
    }

    [Fact]
    public void Preferred_carrier_receives_the_configured_bonus_and_an_explanation()
    {
        var preferred = Candidate("ABC", "ABC", 40000m, preferred: true, otd: 90m, otp: 90m, placement: 90m, pod: 90m, tender: 90m, claims: 1m);
        var plain = Candidate("DEF", "DEF", 40000m, preferred: false, otd: 90m, otp: 90m, placement: 90m, pod: 90m, tender: 90m, claims: 1m);

        var ranked = RecommendationScorer.Rank([preferred, plain], Weights, Scoring, minimumSample: 20);

        ranked[0].Candidate.Code.Should().Be("ABC");
        ranked[0].RecommendationScore.Should().Be(ranked[1].RecommendationScore + Scoring.PreferredBonus);
        ranked[0].Explanations.Should().Contain(e => e.Contains("Preferred carrier"));
    }

    [Fact]
    public void Insufficient_sample_is_scored_neutrally_and_does_not_outrank_a_proven_carrier()
    {
        var proven = Candidate("ABC", "ABC", 40000m, preferred: false, otd: 92m, otp: 92m, placement: 92m, pod: 92m, tender: 92m, claims: 1m);
        var unproven = Candidate("NEW", "New Carrier", 40000m, preferred: false, otd: 100m, otp: 100m, placement: 100m, pod: 100m, tender: 100m, claims: 0m,
            sample: 3m);

        var ranked = RecommendationScorer.Rank([unproven, proven], Weights, Scoring, minimumSample: 20);

        ranked[0].Candidate.Code.Should().Be("ABC", "a 100% rate on 3 deliveries is not proof of service");
        var otd = ranked.Single(r => r.Candidate.Code == "NEW").Components.Single(c => c.Factor == "On-time delivery");
        otd.Sufficient.Should().BeFalse();
        otd.Score.Should().Be(Scoring.InsufficientDataScore);
        otd.Basis.Should().Contain("Insufficient data (n=3, minimum 20)");
    }

    [Fact]
    public void Ranking_is_empty_when_nobody_is_eligible()
    {
        RecommendationScorer.Rank([], Weights, Scoring, minimumSample: 20).Should().BeEmpty();
    }

    // ---------- eligibility reasons ----------

    [Fact]
    public void Transporter_with_a_configured_lane_rate_and_vehicle_is_eligible()
    {
        var data = BaseData(out var transporter);

        var result = Evaluate(data, Request());

        result.Single().Eligible.Should().BeTrue();
        result.Single().Reasons.Should().BeEmpty();
        result.Single().Rate!.EstimatedCost.Should().Be(42000m);
    }

    [Fact]
    public void Missing_lane_is_explained()
    {
        var data = BaseData(out _) with { Lanes = [] };

        var result = Evaluate(data, Request()).Single();

        result.Eligible.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.StartsWith("Lane not configured"));
    }

    [Fact]
    public void Missing_required_capability_is_explained()
    {
        var data = BaseData(out _);

        var result = Evaluate(data, Request(caps: ["HAZARDOUS"])).Single();

        result.Reasons.Should().Contain("Required capability HAZARDOUS is not held.");
    }

    [Fact]
    public void Insufficient_vehicle_capacity_reports_the_largest_available()
    {
        var data = BaseData(out _) with
        {
            Vehicles = [Vehicle(7500m)]
        };

        var result = Evaluate(data, Request(weight: 12000m)).Single();

        result.Reasons.Should().ContainSingle(r => r.Contains("capacity") && r.Contains("7,500") || r.Contains("largest available 7500"));
    }

    [Fact]
    public void Missing_rate_is_explained()
    {
        var data = BaseData(out _) with { Rates = [] };

        var result = Evaluate(data, Request()).Single();

        result.Reasons.Should().Contain(r => r.StartsWith("No applicable rate"));
    }

    [Fact]
    public void Suspended_transporter_is_excluded_with_a_reason()
    {
        var data = BaseData(out var transporter);
        transporter.Status = TransporterStatus.Suspended;

        var result = Evaluate(data, Request()).Single();

        result.Reasons.Should().Contain("Transporter is suspended.");
    }

    [Fact]
    public void Restricted_lane_rule_excludes_the_transporter()
    {
        var data = BaseData(out var transporter);
        var lane = data.Lanes.Single();
        var rules = new[]
        {
            new TransporterPlanningRule
            {
                TransporterId = transporter.Id, RuleType = PlanningRuleType.Restricted, LaneReference = lane.Id,
                Reason = "Repeated damage on this route", EffectiveFrom = new DateTime(2026, 1, 1), IsActive = true
            }
        };

        var result = Evaluate(data with { PlanningRules = rules }, Request()).Single();

        result.Eligible.Should().BeFalse();
        result.RestrictedForPlanning.Should().BeTrue();
        result.Reasons.Should().Contain(r => r.Contains("Repeated damage on this route"));
    }

    [Fact]
    public void Avoid_for_urgent_rule_applies_only_to_urgent_loads()
    {
        var data = BaseData(out var transporter);
        var rules = new[]
        {
            new TransporterPlanningRule
            {
                TransporterId = transporter.Id, RuleType = PlanningRuleType.AvoidForUrgent,
                Reason = "Slow at night", EffectiveFrom = new DateTime(2026, 1, 1), IsActive = true
            }
        };
        var withRule = data with { PlanningRules = rules };

        Evaluate(withRule, Request(urgent: false)).Single().Eligible.Should().BeTrue();
        Evaluate(withRule, Request(urgent: true)).Single().Reasons.Should().Contain(r => r.Contains("urgent"));
    }

    [Fact]
    public void Transit_sla_longer_than_the_delivery_window_is_explained()
    {
        var data = BaseData(out _) with
        {
            Lanes = [Lane(sla: 3 * 24 * 60)]
        };
        var request = Request() with
        {
            PickupBy = new DateTime(2026, 10, 10, 8, 0, 0),
            DeliverBy = new DateTime(2026, 10, 10, 20, 0, 0)
        };

        var result = Evaluate(data, request).Single();

        result.Reasons.Should().Contain(r => r.StartsWith("Transit SLA of 72 h exceeds"));
    }

    [Fact]
    public void Mandatory_compliance_gap_makes_the_transporter_ineligible()
    {
        var data = BaseData(out _) with
        {
            DocumentTypes = [new DocumentType { Id = 1, Code = "GST_CERT", Name = "GST Certificate", IsMandatory = true }],
            Documents = []
        };

        var result = Evaluate(data, Request()).Single();

        result.Eligible.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.StartsWith("Compliance:") && r.Contains("GST Certificate"));
    }

    // ---------- helpers ----------

    private static readonly RecommendationWeightsSetting Weights = new(
        Rate: 30, OnTimePickup: 20, OnTimeDelivery: 15, PlacementCompliance: 10, PodCompliance: 5,
        TenderAcceptance: 5, ClaimsRate: 5, Availability: 10);

    private static readonly RecommendationScoringSetting Scoring = new(
        ClaimsZeroAtPct: 5, AvailabilityPerVehicle: 40, PreferredBonus: 5, InsufficientDataScore: 60);

    private static readonly EligibilityParameters Parameters = new(
        RequestDate, new DateOnly(2026, 10, 4), 30, 90, 20, new EligibilityRestrictionsSetting(false, 90, 3));

    private static TransporterSelectionRequest Request(decimal weight = 12000m, bool urgent = false, string[]? caps = null) =>
        new(1, 2, 4, "FTL", weight, null, RequestDate, caps ?? [], urgent);

    private static IReadOnlyList<CandidateEvaluation> Evaluate(EligibilityData data, TransporterSelectionRequest request) =>
        TransporterEligibilityEvaluator.Evaluate(request, data, Parameters);

    private static EligibilityData BaseData(out Transporter transporter)
    {
        transporter = new Transporter { Id = 1, TransporterCode = "ABC", LegalName = "ABC Logistics", Status = TransporterStatus.Active };
        return new EligibilityData(
            Transporters: [transporter],
            Lanes: [Lane()],
            Rates: [new TransporterRate
            {
                Id = 9, TransporterId = 1, OriginLocationReference = 1, DestinationLocationReference = 2, VehicleTypeReference = 4,
                ServiceType = "FTL", RateType = RateType.PerTrip, RateValue = 42000m, Currency = "INR",
                EffectiveFrom = new DateTime(2026, 1, 1), Status = RecordStatus.Active
            }],
            Vehicles: [Vehicle(12000m)],
            Capabilities: [],
            Documents: [],
            DocumentTypes: [],
            PlanningRules: [],
            Kpis: []);
    }

    private static TransporterLane Lane(int? sla = 1440) => new()
    {
        Id = 5, TransporterId = 1, OriginLocationReference = 1, DestinationLocationReference = 2,
        ServiceType = "FTL", VehicleTypeReference = 4, TransitSlaMinutes = sla,
        EffectiveFrom = new DateTime(2026, 1, 1), Status = RecordStatus.Active
    };

    private static TransporterVehicle Vehicle(decimal capacity) => new()
    {
        Id = 30, TransporterId = 1, RegistrationNumber = "MH12AB1234", VehicleTypeReference = 4,
        PayloadCapacityKg = capacity, Status = RecordStatus.Active, AvailabilityStatus = VehicleAvailabilityStatus.Available
    };

    private static PerformanceKpi Kpi(KpiType type, decimal value, decimal denominator, long? lane) => new()
    {
        TransporterId = 1, KpiType = type, LaneReference = lane, KpiValue = value, Denominator = denominator,
        Numerator = Math.Round(value * denominator / 100m, 2),
        PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 9, 30), CalculationVersion = 1
    };

    private static CandidateEvaluation Candidate(
        string code, string name, decimal cost, bool preferred,
        decimal otd, decimal otp, decimal placement, decimal pod, decimal tender, decimal claims,
        decimal sample = 120m)
    {
        var kpis = Enum.GetValues<KpiType>().ToDictionary(t => t, _ => new KpiPoint(null, 0, false, "None"));
        void Set(KpiType type, decimal value) => kpis[type] = new KpiPoint(value, sample, sample >= 20, "Transporter");
        Set(KpiType.OnTimeDelivery, otd);
        Set(KpiType.OnTimePickup, otp);
        Set(KpiType.PlacementCompliance, placement);
        Set(KpiType.PodCompliance, pod);
        Set(KpiType.TenderAcceptance, tender);
        Set(KpiType.ClaimsRate, claims);

        return new CandidateEvaluation(
            TransporterId: code.GetHashCode(), Code: code, Name: name, Status: TransporterStatus.Active,
            Eligible: true, Reasons: [], Warnings: [], LaneId: 5, Preferred: preferred, RestrictedForPlanning: false,
            Rate: new RateQuote(1, RateType.PerTrip, cost, cost, "INR", null), AvailableVehicles: 1,
            ComplianceStatus: ComplianceOverallStatus.Compliant, Kpis: kpis);
    }
}
