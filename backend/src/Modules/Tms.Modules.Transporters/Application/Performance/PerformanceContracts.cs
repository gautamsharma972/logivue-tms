using FluentValidation;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Transporters.Application.Performance;

public sealed record PeriodRequest(DateOnly From, DateOnly To);

public sealed record KpiDto(KpiType Kpi, decimal Numerator, decimal Denominator, decimal? Value);

public sealed record MonthDto(DateOnly Month, IReadOnlyList<KpiDto> Kpis);

/// <summary>The live KPIs and metrics for a period, with the stored month-by-month trend.</summary>
public sealed record PerformanceDto(Guid TransporterId, DateOnly From, DateOnly To, IReadOnlyList<KpiDto> Kpis, OperationalMetrics Metrics, IReadOnlyList<MonthDto> Months);

public sealed record ScorecardKpiDto(KpiType Kpi, decimal? Value, decimal Weight, decimal? WeightedScore, decimal Numerator, decimal Denominator);

/// <param name="OverallScore">Weighted average of the measured KPIs. Null when nothing is measurable.</param>
public sealed record ScorecardDto(
    Guid Id, Guid TransporterId, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? OverallScore, DateTimeOffset GeneratedAt, int CalculationVersion, IReadOnlyList<ScorecardKpiDto> Kpis);

public enum RankingMetric
{
    OverallScore,
    OnTimePickup,
    OnTimeDelivery,
    PlacementCompliance,
    TenderAcceptance,
    PodCompliance,
    ClaimsRate,
    CostPerformance,
    Availability,
}

/// <summary>
/// Where the KPIs come from. No lane and no vehicle type means transporter-level KPIs. A lane means every transporter's KPIs for lanes with the same
/// origin and destination. Region is the transporter's state; Mode is the service it offers.
/// </summary>
public sealed record RankingQuery(
    DateOnly From, DateOnly To, RankingMetric Metric = RankingMetric.OverallScore, Guid? LaneId = null, Guid? VehicleTypeId = null, string? Region = null, ServiceModes? Mode = null);

public sealed record BenchmarkQuery(DateOnly From, DateOnly To, Guid? LaneId = null, Guid? VehicleTypeId = null);

public sealed record RankedKpiDto(KpiType Kpi, decimal? Value, decimal Numerator, decimal Denominator);

/// <param name="Rank">1 is best. Null for transporters that are not ranked.</param>
/// <param name="MetricValue">The value the ranking is sorted on: the overall score, or the chosen KPI's percentage.</param>
/// <param name="Note">Why a transporter is not ranked, such as a sample below the minimum.</param>
public sealed record RankedTransporterDto(
    int? Rank, Guid TransporterId, string TransporterCode, string TransporterName, string? Region, TransporterStatus Status, decimal? MetricValue, decimal? OverallScore,
    bool Ranked, string? Note, IReadOnlyList<RankedKpiDto> Kpis);

public sealed record RankingResultDto(RankingMetric Metric, string ScopeLabel, DateOnly From, DateOnly To, IReadOnlyList<RankedTransporterDto> Rows);

/// <summary>One benchmark line. Gaps are signed so that negative always means behind the comparison, including for claims.</summary>
public sealed record BenchmarkRowDto(
    KpiType Kpi, decimal? Transporter, decimal? LaneAverage, decimal? RegionAverage, decimal? ModeAverage, decimal? TopPerformer, Guid? TopPerformerTransporterId, decimal? GapToTop, decimal? GapToLaneAverage);

public sealed record BenchmarkDto(Guid TransporterId, string ScopeLabel, DateOnly From, DateOnly To, IReadOnlyList<BenchmarkRowDto> Rows);

public sealed record ExecutionEventDto(ExecutionEventType EventType, DateTimeOffset EventAt, string? DelayReasonCode, string? Remarks);

/// <summary>Planned and actual pickup and delivery for one load, with the stored delay and attribution for each. Null minutes mean the event has not happened or has no planned time.</summary>
public sealed record ExecutionDto(
    Guid Id, Guid ShipmentId, string ShipmentNumber, Guid TransporterId,
    DateTimeOffset? PlannedPickupAt, DateTimeOffset? ActualPickupAt, int? PickupDelayMinutes, string? PickupDelayReasonCode, DelayAttribution PickupAttribution,
    DateTimeOffset? PlannedDeliveryAt, DateTimeOffset? ActualDeliveryAt, int? DeliveryDelayMinutes, string? DeliveryDelayReasonCode, DelayAttribution DeliveryAttribution,
    ExecutionStatus Status, IReadOnlyList<ExecutionEventDto> Events);

public sealed record CreateExecutionRequest(Guid ShipmentId);

public sealed record RecordExecutionEventRequest(ExecutionEventType EventType, DateTimeOffset EventAt, string? DelayReasonCode = null, string? Remarks = null);

/// <param name="Delivery">True to give the delivery delay a reason, false for the pickup delay.</param>
public sealed record AttributeDelayRequest(bool Delivery, string ReasonCode);

public sealed record LaneDto(
    Guid Id, Guid TransporterId, string OriginState, string? OriginCity, string DestinationState, string? DestinationCity, FreightMode? Mode, int? TransitSlaMinutes,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, long Version);

public sealed record SaveLaneRequest(
    string OriginState, string? OriginCity, string DestinationState, string? DestinationCity, FreightMode? Mode, int? TransitSlaMinutes,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, long? Version);

public sealed record SettingDto(string Key, System.Text.Json.JsonElement Value, bool IsDefault);

public sealed record SaveSettingRequest(System.Text.Json.JsonElement Value);

internal sealed class PeriodRequestValidator : AbstractValidator<PeriodRequest>
{
    public PeriodRequestValidator()
    {
        RuleFor(x => x.From).NotEmpty();
        RuleFor(x => x.To).NotEmpty().GreaterThanOrEqualTo(x => x.From).WithMessage("The end must be on or after the start.");
    }
}

internal sealed class RecordExecutionEventRequestValidator : AbstractValidator<RecordExecutionEventRequest>
{
    public RecordExecutionEventRequestValidator()
    {
        RuleFor(x => x.EventType).IsInEnum();
        RuleFor(x => x.EventAt).NotEmpty();
        RuleFor(x => x.Remarks).MaximumLength(500);
        RuleFor(x => x.DelayReasonCode).MaximumLength(40);
    }
}

internal sealed class AttributeDelayRequestValidator : AbstractValidator<AttributeDelayRequest>
{
    public AttributeDelayRequestValidator() => RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(40);
}
