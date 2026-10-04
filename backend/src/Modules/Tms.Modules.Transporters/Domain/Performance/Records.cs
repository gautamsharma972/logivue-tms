using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

/// <summary>A load offered to a transporter and how they answered. The source of the tender-acceptance KPI.</summary>
public sealed class TenderInvitation : Entity, ITenantScoped
{
    private TenderInvitation()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string ShipmentNumber { get; private set; } = null!;

    public Guid TransporterId { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public InvitationOutcome Outcome { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public string? Reason { get; private set; }

    // Where the load goes, copied when it is offered: a declined load loses its transporter on the shipment, so the lane would be lost.
    public Guid? VehicleTypeId { get; private set; }

    public FreightMode? Mode { get; private set; }

    public string? OriginState { get; private set; }

    public string? OriginCity { get; private set; }

    public string? DestinationState { get; private set; }

    public string? DestinationCity { get; private set; }

    public static TenderInvitation Create(Guid tenantId, ShipmentFact? fact, Guid shipmentId, string number, Guid transporterId, DateTimeOffset sentAt) => new()
    {
        TenantId = tenantId, ShipmentId = shipmentId, ShipmentNumber = number, TransporterId = transporterId, SentAt = sentAt, Outcome = InvitationOutcome.Open,
        VehicleTypeId = fact?.VehicleTypeId, Mode = fact?.Mode, OriginState = fact?.OriginState, OriginCity = fact?.OriginCity, DestinationState = fact?.DestinationState, DestinationCity = fact?.DestinationCity,
    };

    public bool Respond(InvitationOutcome outcome, DateTimeOffset at, string? reason)
    {
        if (Outcome != InvitationOutcome.Open)
        {
            return false;
        }

        Outcome = outcome;
        RespondedAt = at;
        Reason = reason?.Trim();
        return true;
    }
}

/// <summary>One recorded milestone of a load.</summary>
public sealed class ExecutionEvent : Entity, ITenantScoped
{
    private ExecutionEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid LoadExecutionId { get; private set; }

    public ExecutionEventType EventType { get; private set; }

    public DateTimeOffset EventAt { get; private set; }

    public string? DelayReasonCode { get; private set; }

    public string? Remarks { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    internal static ExecutionEvent Create(Guid tenantId, Guid executionId, ExecutionEventType type, DateTimeOffset at, string? reasonCode, string? remarks, Guid? by, DateTimeOffset now) => new()
    {
        TenantId = tenantId, LoadExecutionId = executionId, EventType = type, EventAt = at, DelayReasonCode = reasonCode,
        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(), RecordedBy = by, RecordedAt = now,
    };
}

/// <summary>
/// The execution record of one accepted load. Planned times come from the shipment; actual times from recorded events. Delay minutes
/// and attribution are stored when the event is recorded, so history does not change when the delay policy is later edited.
/// </summary>
public sealed class LoadExecution : AggregateRoot, ITenantScoped
{
    private readonly List<ExecutionEvent> _events = [];

    private LoadExecution()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string ShipmentNumber { get; private set; } = null!;

    public Guid TransporterId { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    public FreightMode Mode { get; private set; }

    public string? OriginState { get; private set; }

    public string? OriginCity { get; private set; }

    public string? DestinationState { get; private set; }

    public string? DestinationCity { get; private set; }

    public DateTimeOffset? PlannedPickupAt { get; private set; }

    public DateTimeOffset? ActualPickupAt { get; private set; }

    public int? PickupDelayMinutes { get; private set; }

    public string? PickupDelayReasonCode { get; private set; }

    public DelayAttribution PickupAttribution { get; private set; }

    public DateTimeOffset? PlannedDeliveryAt { get; private set; }

    public DateTimeOffset? ActualDeliveryAt { get; private set; }

    public int? DeliveryDelayMinutes { get; private set; }

    public string? DeliveryDelayReasonCode { get; private set; }

    public DelayAttribution DeliveryAttribution { get; private set; }

    public ExecutionStatus Status { get; private set; }

    public IReadOnlyList<ExecutionEvent> Events => _events;

    public static LoadExecution Create(Guid tenantId, ShipmentFact fact, DateTimeOffset? plannedPickupAt, DateTimeOffset? plannedDeliveryAt) => new()
    {
        TenantId = tenantId, ShipmentId = fact.ShipmentId, ShipmentNumber = fact.Number, TransporterId = fact.TransporterId, VehicleTypeId = fact.VehicleTypeId, Mode = fact.Mode,
        OriginState = fact.OriginState, OriginCity = fact.OriginCity, DestinationState = fact.DestinationState, DestinationCity = fact.DestinationCity,
        PlannedPickupAt = plannedPickupAt, PlannedDeliveryAt = plannedDeliveryAt,
    };

    public bool Has(ExecutionEventType type) => _events.Any(e => e.EventType == type);

    /// <summary>
    /// Records a milestone. Each type is recorded once, later than the ones before it. Departure and delivery are measured against the
    /// planned time: early or on time carries no attribution; late carries the attribution of its reason, or <see cref="DelayAttribution.Unattributed"/>.
    /// </summary>
    public Result Record(ExecutionEventType type, DateTimeOffset at, DelayReason? reason, int toleranceMinutes, string? remarks, Guid? by, DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
        {
            return Error.Validation("executions.event_invalid", "Choose a valid event.");
        }

        if (_events.Count > 0)
        {
            var latest = _events.OrderByDescending(e => e.EventAt).First();
            var furthest = _events.MaxBy(e => (int)e.EventType)!;
            if ((int)furthest.EventType >= (int)type)
            {
                return Error.Conflict("executions.sequence_invalid", $"{type} cannot be recorded after {furthest.EventType}.");
            }

            if (at < latest.EventAt)
            {
                return Error.Conflict("executions.time_order", "An event cannot be earlier than the previous event on the same load.");
            }
        }

        _events.Add(ExecutionEvent.Create(TenantId, Id, type, at, reason?.Code, remarks, by, now));
        switch (type)
        {
            case ExecutionEventType.VehicleDeparture:
                ActualPickupAt = at;
                (PickupDelayMinutes, PickupDelayReasonCode, PickupAttribution) = Evaluate(PlannedPickupAt, at, toleranceMinutes, reason);
                break;
            case ExecutionEventType.DeliveryComplete:
                ActualDeliveryAt = at;
                (DeliveryDelayMinutes, DeliveryDelayReasonCode, DeliveryAttribution) = Evaluate(PlannedDeliveryAt, at, toleranceMinutes, reason);
                break;
        }

        var next = type switch
        {
            ExecutionEventType.PickupAppointment or ExecutionEventType.VehicleArrival or ExecutionEventType.LoadingStart or ExecutionEventType.LoadingComplete => ExecutionStatus.AtPickup,
            ExecutionEventType.DeliveryComplete => ExecutionStatus.Delivered,
            _ => ExecutionStatus.PickedUp,
        };
        if (next > Status)
        {
            Status = next;
        }

        return Result.Success();
    }

    public void Cancel() => Status = ExecutionStatus.Cancelled;

    /// <summary>Gives a late event a reason after the fact (the system records the time; a person says why). Changes attribution, never the minutes.</summary>
    public Result Attribute(bool delivery, DelayReason reason)
    {
        var minutes = delivery ? DeliveryDelayMinutes : PickupDelayMinutes;
        var current = delivery ? DeliveryAttribution : PickupAttribution;
        if (minutes is null || current == DelayAttribution.None)
        {
            return Error.Conflict("executions.not_late", "That event was not late, so there is nothing to attribute.");
        }

        if (delivery)
        {
            DeliveryDelayReasonCode = reason.Code;
            DeliveryAttribution = reason.Attribution;
        }
        else
        {
            PickupDelayReasonCode = reason.Code;
            PickupAttribution = reason.Attribution;
        }

        return Result.Success();
    }

    public static (int? Minutes, string? ReasonCode, DelayAttribution Attribution) Evaluate(DateTimeOffset? planned, DateTimeOffset actual, int toleranceMinutes, DelayReason? reason)
    {
        if (planned is null)
        {
            return (null, null, DelayAttribution.None);
        }

        var minutes = (int)Math.Round((actual - planned.Value).TotalMinutes);
        if (minutes <= toleranceMinutes)
        {
            return (minutes, null, DelayAttribution.None);
        }

        return (minutes, reason?.Code, reason?.Attribution ?? DelayAttribution.Unattributed);
    }
}

public sealed record DelayReason(string Code, string Name, DelayAttribution Attribution);

/// <summary>A KPI for a month, stored with its numerator and denominator so every value stays auditable. A null value means not measurable, never zero.</summary>
public sealed class PerformanceKpi : Entity, ITenantScoped
{
    private PerformanceKpi()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public Guid? LaneId { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    public KpiType KpiType { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public decimal Numerator { get; private set; }

    public decimal Denominator { get; private set; }

    public decimal? KpiValue { get; private set; }

    public int CalculationVersion { get; private set; }

    public static PerformanceKpi Create(
        Guid tenantId, Guid transporterId, Guid? laneId, Guid? vehicleTypeId, KpiType type, DateOnly start, DateOnly end, decimal numerator, decimal denominator, decimal? value, int version) => new()
    {
        TenantId = tenantId, TransporterId = transporterId, LaneId = laneId, VehicleTypeId = vehicleTypeId, KpiType = type, PeriodStart = start, PeriodEnd = end,
        Numerator = numerator, Denominator = denominator, KpiValue = value, CalculationVersion = version,
    };
}

/// <summary>One KPI as it stood when a scorecard was generated, with the weight then in force.</summary>
public sealed record ScorecardLine(KpiType Kpi, decimal? Value, decimal Numerator, decimal Denominator, decimal Weight, decimal? WeightedScore);

/// <summary>A generated scorecard. Immutable: regenerating adds another, so earlier ones keep the formula and weights they used.</summary>
public sealed class Scorecard : Entity, ITenantScoped
{
    private Scorecard()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public decimal? OverallScore { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public Guid? GeneratedBy { get; private set; }

    public int CalculationVersion { get; private set; }

    public IReadOnlyList<ScorecardLine> Lines { get; private set; } = [];

    public static Scorecard Create(
        Guid tenantId, Guid transporterId, DateOnly start, DateOnly end, decimal? overall, IReadOnlyList<ScorecardLine> lines, DateTimeOffset now, Guid? by, int version) => new()
    {
        TenantId = tenantId, TransporterId = transporterId, PeriodStart = start, PeriodEnd = end, OverallScore = overall, Lines = lines, GeneratedAt = now,
        GeneratedBy = by, CalculationVersion = version,
    };
}

/// <summary>A lane a transporter serves: where from, where to, and the transit time they commit to. The grouping for lane KPIs and rankings.</summary>
public sealed class TransporterLane : AggregateRoot, ITenantScoped
{
    private TransporterLane()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string OriginState { get; private set; } = null!;

    /// <summary>Null means any city in the state.</summary>
    public string? OriginCity { get; private set; }

    public string DestinationState { get; private set; } = null!;

    public string? DestinationCity { get; private set; }

    /// <summary>Null means both full-truck and part-load.</summary>
    public FreightMode? Mode { get; private set; }

    public int? TransitSlaMinutes { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<TransporterLane> Create(
        Guid tenantId, Guid transporterId, string originState, string? originCity, string destinationState, string? destinationCity, FreightMode? mode,
        int? transitSlaMinutes, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        var lane = new TransporterLane { TenantId = tenantId, TransporterId = transporterId };
        var set = lane.Set(originState, originCity, destinationState, destinationCity, mode, transitSlaMinutes, effectiveFrom, effectiveTo, true);
        return set.IsFailure ? set.Error : lane;
    }

    public Result Set(
        string originState, string? originCity, string destinationState, string? destinationCity, FreightMode? mode, int? transitSlaMinutes,
        DateOnly effectiveFrom, DateOnly? effectiveTo, bool isActive)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(originState))
        {
            errors["originState"] = ["Choose the origin state."];
        }

        if (string.IsNullOrWhiteSpace(destinationState))
        {
            errors["destinationState"] = ["Choose the destination state."];
        }

        if (mode is { } m && !Enum.IsDefined(m))
        {
            errors["mode"] = ["Choose full truck, part load or both."];
        }

        if (transitSlaMinutes is <= 0 or > 20_000)
        {
            errors["transitSlaMinutes"] = ["Transit time must be a positive number of minutes."];
        }

        if (effectiveTo is { } to && to < effectiveFrom)
        {
            errors["effectiveTo"] = ["The end date must be on or after the start date."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        OriginState = Normalise(originState)!;
        OriginCity = Normalise(originCity);
        DestinationState = Normalise(destinationState)!;
        DestinationCity = Normalise(destinationCity);
        Mode = mode;
        TransitSlaMinutes = transitSlaMinutes;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
        return Result.Success();
    }

    /// <summary>True when a load going from one place to another is on this lane.</summary>
    public bool Covers(string originState, string? originCity, string? destinationState, string? destinationCity, FreightMode mode) =>
        IsActive
        && (Mode is null || Mode == mode)
        && Same(OriginState, originState) && (OriginCity is null || Same(OriginCity, originCity))
        && destinationState is not null && Same(DestinationState, destinationState) && (DestinationCity is null || Same(DestinationCity, destinationCity));

    /// <summary>True when the lane is in service on a date.</summary>
    public bool InServiceOn(DateOnly date) => IsActive && EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    /// <summary>Two lanes are the same route if they join the same places for the same service; they may not overlap in time.</summary>
    public bool SameRouteAs(TransporterLane other) =>
        Same(OriginState, other.OriginState) && Same(OriginCity, other.OriginCity) && Same(DestinationState, other.DestinationState)
        && Same(DestinationCity, other.DestinationCity) && Mode == other.Mode;

    public bool OverlapsPeriod(DateOnly from, DateOnly? to) =>
        EffectiveFrom <= (to ?? DateOnly.MaxValue) && (EffectiveTo ?? DateOnly.MaxValue) >= from;

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

/// <summary>Key/value business configuration (KPI weights, SLAs, thresholds) as JSON, so each setting can hold a structured object.</summary>
public sealed class TransporterSetting : AggregateRoot, ITenantScoped
{
    private TransporterSetting()
    {
    }

    public Guid TenantId { get; private set; }

    public string Key { get; private set; } = null!;

    public string ValueJson { get; private set; } = "{}";

    public static TransporterSetting Create(Guid tenantId, string key, string json) => new() { TenantId = tenantId, Key = key, ValueJson = json };

    public void Change(string json) => ValueJson = json;
}
