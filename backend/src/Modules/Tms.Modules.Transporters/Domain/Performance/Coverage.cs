using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

public enum PlanningRuleType
{
    /// <summary>Favoured wherever it is eligible.</summary>
    PreferredCarrier = 1,

    /// <summary>Favoured on one lane.</summary>
    PreferredLane = 2,

    /// <summary>Not given urgent loads.</summary>
    AvoidForUrgent = 3,

    /// <summary>Not given loads, on one lane or everywhere.</summary>
    Restricted = 4,

    /// <summary>Not given loads at all, on one lane or everywhere (a harder "no" than Restricted: the reason is a decision, not a preference).</summary>
    DoNotAllocate = 5,
}

/// <summary>What a transporter can be asked to carry beyond ordinary freight. A fixed catalogue so loads and carriers use the same words.</summary>
public static class CapabilityCatalog
{
    public const string Hazardous = "HAZARDOUS";
    public const string TemperatureControlled = "TEMPERATURE_CONTROLLED";
    public const string OverDimensional = "OVER_DIMENSIONAL";
    public const string HighValue = "HIGH_VALUE";
    public const string FoodGrade = "FOOD_GRADE";
    public const string Odc = "ODC";
    public const string Fragile = "FRAGILE";
    public const string HeavyCargo = "HEAVY_CARGO";
    public const string ReverseLogistics = "REVERSE_LOGISTICS";
    public const string MilkRun = "MILK_RUN";
    public const string Express = "EXPRESS";
    public const string Dedicated = "DEDICATED";

    public static IReadOnlyList<(string Code, string Name)> Items { get; } =
    [
        (Hazardous, "Hazardous goods"),
        (TemperatureControlled, "Temperature controlled"),
        (OverDimensional, "Over-dimensional cargo"),
        (HighValue, "High-value goods"),
        (FoodGrade, "Food-grade transport"),
        (Fragile, "Fragile goods"),
        (HeavyCargo, "Heavy cargo"),
        (ReverseLogistics, "Reverse logistics"),
        (MilkRun, "Milk runs"),
        (Express, "Express"),
        (Dedicated, "Dedicated fleet"),
    ];

    public static bool IsKnown(string code) => Items.Any(i => string.Equals(i.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A capability a transporter holds for a period. Removing one only ends it, so past eligibility stays explainable.</summary>
public sealed class TransporterCapability : AggregateRoot, ITenantScoped
{
    private TransporterCapability()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string Code { get; private set; } = null!;

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<TransporterCapability> Create(Guid tenantId, Guid transporterId, string code, DateOnly from, DateOnly? to)
    {
        var normal = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normal.Length is 0 or > 40 || !normal.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
        {
            return Error.Validation("capabilities.unknown", "Choose a capability from the list.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["code"] = ["Choose a capability from the list."] },
            };
        }

        if (to is { } end && end < from)
        {
            return Error.Validation("capabilities.dates", "The end date must be on or after the start date.");
        }

        return new TransporterCapability { TenantId = tenantId, TransporterId = transporterId, Code = normal, EffectiveFrom = from, EffectiveTo = to };
    }

    public bool HeldOn(DateOnly date) => IsActive && EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    public Result End(DateOnly today)
    {
        if (!IsActive)
        {
            return Error.Conflict("capabilities.already_ended", "This capability has already been removed.");
        }

        IsActive = false;
        EffectiveTo = today;
        return Result.Success();
    }
}

/// <summary>A decision about how a transporter is used in planning: preferred, avoided for urgent loads, restricted or not to be allocated. Always with a reason.</summary>
public sealed class PlanningRule : AggregateRoot, ITenantScoped
{
    private PlanningRule()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public PlanningRuleType RuleType { get; private set; }

    /// <summary>Null means the rule applies to every lane.</summary>
    public Guid? LaneId { get; private set; }

    public string Reason { get; private set; } = null!;

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public bool IsActive { get; private set; } = true;

    public string? EndedBecause { get; private set; }

    public static Result<PlanningRule> Create(Guid tenantId, Guid transporterId, PlanningRuleType type, Guid? laneId, string reason, DateOnly from, DateOnly? to)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(type))
        {
            errors["ruleType"] = ["Choose the kind of rule."];
        }

        if (type == PlanningRuleType.PreferredLane && laneId is null)
        {
            errors["laneId"] = ["A preferred-lane rule needs the lane."];
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
        {
            errors["reason"] = ["Say why (up to 300 characters)."];
        }

        if (to is { } end && end < from)
        {
            errors["effectiveTo"] = ["The end date must be on or after the start date."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        return new PlanningRule { TenantId = tenantId, TransporterId = transporterId, RuleType = type, LaneId = laneId, Reason = reason.Trim(), EffectiveFrom = from, EffectiveTo = to };
    }

    public bool AppliesOn(DateOnly date) => IsActive && EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    public bool IsExclusion => RuleType is PlanningRuleType.Restricted or PlanningRuleType.DoNotAllocate;

    public Result End(string reason, DateOnly today)
    {
        if (!IsActive)
        {
            return Error.Conflict("planning_rules.already_ended", "This rule has already been ended.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("planning_rules.reason_required", "Say why the rule is being ended.");
        }

        IsActive = false;
        EffectiveTo = today;
        EndedBecause = reason.Trim();
        return Result.Success();
    }
}

/// <summary>The latest scores per transporter (and optionally lane), kept so planning can read them cheaply. The authoritative figures stay in the KPI rows.</summary>
public sealed class PlanningFeedback : Entity, ITenantScoped
{
    private PlanningFeedback()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public decimal? OverallScore { get; private set; }

    public decimal? OtpPct { get; private set; }

    public decimal? OtdPct { get; private set; }

    public decimal? PlacementCompliancePct { get; private set; }

    public decimal? PodCompliancePct { get; private set; }

    public decimal? TenderAcceptancePct { get; private set; }

    public decimal? ClaimsRatePct { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PlanningFeedback For(Guid tenantId, Guid transporterId) => new() { TenantId = tenantId, TransporterId = transporterId };

    public void Refresh(decimal? overall, IReadOnlyDictionary<KpiType, decimal?> values, DateTimeOffset now)
    {
        OverallScore = overall;
        OtpPct = values.GetValueOrDefault(KpiType.OnTimePickup);
        OtdPct = values.GetValueOrDefault(KpiType.OnTimeDelivery);
        PlacementCompliancePct = values.GetValueOrDefault(KpiType.PlacementCompliance);
        PodCompliancePct = values.GetValueOrDefault(KpiType.PodCompliance);
        TenderAcceptancePct = values.GetValueOrDefault(KpiType.TenderAcceptance);
        ClaimsRatePct = values.GetValueOrDefault(KpiType.ClaimsRate);
        UpdatedAt = now;
    }
}
