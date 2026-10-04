using FluentAssertions;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Scorecards;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Claims, cost and availability KPIs, and the scoring rules they feed. No database or host.</summary>
public class OperationalSourcesUnitTests
{
    private static readonly RecommendationScoringSetting Scoring = new(ClaimsZeroAtPct: 2m, AvailabilityPerVehicle: 10m, PreferredBonus: 5m, InsufficientDataScore: 50m);

    private static readonly KpiWeightsSetting Weights = new(OnTimePickup: 10, OnTimeDelivery: 25, PlacementCompliance: 15,
        TenderAcceptance: 10, PodCompliance: 5, ClaimsRate: 10, CostPerformance: 15, Availability: 10);

    private static OperationalInputs Inputs(TransporterClaimsSummaryDto? claims = null, TransporterCostSummaryDto? cost = null,
        TransporterAvailabilitySummaryDto? availability = null) =>
        new(new DateTime(2026, 6, 1), new DateTime(2026, 7, 1), new DateTime(2026, 7, 1), 15, 15,
            new List<Tender>(), new List<VehiclePlacementRequest>(), new List<LoadExecution>(),
            new TransporterPodSummaryDto(1, 0, 0, 0, 0, 0, 0, null), claims, cost, availability);

    private static OperationalKpi Kpi(OperationalPeriodResult result, KpiType type) => result.Kpis.Single(k => k.Type == type);

    [Fact]
    public void Claims_cost_and_availability_produce_values_from_their_counts()
    {
        var result = OperationalKpiCalculator.Calculate(Inputs(
            claims: new TransporterClaimsSummaryDto(1, TotalShipments: 40, ClaimsCount: 2, ClaimsRatePct: 5m, ClaimValue: 0m, DamageCount: 0, ShortageCount: 0, LossTheftCount: 0, OpenClaims: 0, ResolvedClaims: 0),
            cost: new TransporterCostSummaryDto(1, LoadsWithCost: 10, OnBudgetLoads: 8, AgreedAmount: 0m, InvoicedAmount: 0m),
            availability: new TransporterAvailabilitySummaryDto(1, VehicleDaysCommitted: 50, VehicleDaysAvailable: 45)),
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Kpi(result, KpiType.ClaimsRate).Value.Should().Be(5m);
        Kpi(result, KpiType.CostPerformance).Value.Should().Be(80m);
        Kpi(result, KpiType.Availability).Value.Should().Be(90m);
    }

    [Fact]
    public void Missing_sources_are_not_measurable_rather_than_zero()
    {
        var result = OperationalKpiCalculator.Calculate(Inputs(), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Kpi(result, KpiType.ClaimsRate).Value.Should().BeNull();
        Kpi(result, KpiType.CostPerformance).Value.Should().BeNull();
        Kpi(result, KpiType.Availability).Value.Should().BeNull();
    }

    [Fact]
    public void Claims_rate_scores_lower_is_better_and_floors_at_zero()
    {
        ScorecardMath.ToScore(KpiType.ClaimsRate, 0m, Scoring).Should().Be(100m);
        ScorecardMath.ToScore(KpiType.ClaimsRate, 1m, Scoring).Should().Be(50m);
        ScorecardMath.ToScore(KpiType.ClaimsRate, 2m, Scoring).Should().Be(0m);
        ScorecardMath.ToScore(KpiType.ClaimsRate, 9m, Scoring).Should().Be(0m);
    }

    [Fact]
    public void A_source_with_too_few_loads_drops_out_and_the_weights_are_renormalised()
    {
        var rows = new List<PerformanceKpi>
        {
            Row(KpiType.OnTimeDelivery, numerator: 96, denominator: 100),
            // Cost has fewer loads than the minimum sample of 20, so it must not count.
            Row(KpiType.CostPerformance, numerator: 5, denominator: 5)
        };

        var measured = ScorecardMath.Measure(rows, Weights, minimumSample: 20, scoring: Scoring);
        var overall = ScorecardMath.Overall(measured);

        measured.Single(m => m.Kpi == KpiType.CostPerformance).Value.Should().BeNull();
        overall.Should().Be(96m);
    }

    [Fact]
    public void Overall_is_null_when_nothing_is_measurable()
    {
        ScorecardMath.Overall(ScorecardMath.Measure([], Weights, 20, Scoring)).Should().BeNull();
    }

    private static PerformanceKpi Row(KpiType type, decimal numerator, decimal denominator) => new()
    {
        TransporterId = 1, KpiType = type, Numerator = numerator, Denominator = denominator,
        PeriodStart = new DateOnly(2026, 6, 1), PeriodEnd = new DateOnly(2026, 6, 30)
    };
}
