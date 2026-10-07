using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum DphFormula
{
    /// <summary>Freight moves by the diesel variation times the fuel share of freight, once the variation passes the threshold.</summary>
    PercentageVariation = 1,

    /// <summary>A fixed rupee amount per trip for every step the diesel price has moved.</summary>
    FixedAdjustment = 2,

    /// <summary>A rupee amount per kilometre for every step the diesel price has moved.</summary>
    PerKmAdjustment = 3,

    /// <summary>Freight follows the diesel index in proportion to the fuel share, with no threshold.</summary>
    Indexed = 4,

    /// <summary>Whole steps of diesel movement each change freight by a set percentage, optionally capped (the classic DPH clause).</summary>
    ThresholdSteps = 5,
}

public enum DphDirection
{
    Both = 1,
    EscalationOnly = 2,
    DeEscalationOnly = 3,
}

/// <summary>Which diesel price counts: the one in force on the shipment day, or the one in force when the period began.</summary>
public enum DphFrequency
{
    Shipment = 1,
    Weekly = 2,
    Fortnightly = 3,
    Monthly = 4,
    Quarterly = 5,
}

/// <summary>Everything one DPH rule version says. Not every field matters to every formula; <see cref="Validate"/> checks the ones the formula uses.</summary>
public sealed record DphRuleSpec(
    string Code,
    string Name,
    DphFormula Formula,
    string Region,
    decimal BaseDieselPrice,
    DateOnly BaseDate,
    decimal FuelComponentPercent,
    decimal ThresholdPercent,
    decimal StepPercent = 0,
    decimal FixedAmountPerStep = 0,
    decimal PerKmPerStep = 0,
    decimal ImpactPercentPerStep = 0,
    decimal? CapPercent = null,
    bool OnExcessOnly = false,
    DphDirection Direction = DphDirection.Both,
    DphFrequency Frequency = DphFrequency.Shipment,
    int AdjustmentDecimals = 2,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    bool IsDefault = false)
{
    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.dph_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length > 40 || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
        {
            return Fail("code", "A code (up to 40 characters) and a name (up to 120) are required.");
        }

        if (!Enum.IsDefined(Formula) || !Enum.IsDefined(Direction) || !Enum.IsDefined(Frequency))
        {
            return Fail("formula", "Choose the formula, direction and frequency.");
        }

        if (string.IsNullOrWhiteSpace(Region) || Region.Length > 64)
        {
            return Fail("region", "Choose the diesel price index region the rule reads.");
        }

        if (BaseDieselPrice is <= 0 or > 500)
        {
            return Fail("baseDieselPrice", "Enter the base diesel price per litre.");
        }

        if (FuelComponentPercent is < 0 or > 100 || ThresholdPercent < 0 || AdjustmentDecimals is < 0 or > 6)
        {
            return Fail("fuelComponentPercent", "The fuel share is 0 to 100%, the threshold cannot be negative, and decimals are 0 to 6.");
        }

        if (CapPercent is <= 0 or > 100)
        {
            return Fail("capPercent", "The cap must be between 0 and 100%.");
        }

        if (EffectiveFrom is { } from && EffectiveTo is { } to && to < from)
        {
            return Fail("effectiveTo", "The rule must end on or after the day it starts.");
        }

        return Formula switch
        {
            DphFormula.PercentageVariation or DphFormula.Indexed when FuelComponentPercent <= 0 => Fail("fuelComponentPercent", "This formula needs a fuel share above zero."),
            DphFormula.ThresholdSteps when StepPercent <= 0 || ImpactPercentPerStep <= 0 || ImpactPercentPerStep > 100 => Fail("stepPercent", "Give the diesel step (in %) and the freight change per step (0 to 100%)."),
            DphFormula.FixedAdjustment when StepPercent <= 0 || FixedAmountPerStep <= 0 => Fail("fixedAmountPerStep", "Give the diesel step (in %) and the rupee amount per step."),
            DphFormula.PerKmAdjustment when StepPercent <= 0 || PerKmPerStep <= 0 => Fail("perKmPerStep", "Give the diesel step (in %) and the rupees per kilometre per step."),
            _ => Result.Success(),
        };
    }
}

/// <summary>One DPH rule version in a contract. A new version of the same code replaces the old one from its effective date; the old one stays for the shipments it priced.</summary>
public sealed class DphRule : Entity, ITenantScoped
{
    private DphRule()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public int Version { get; private set; } = 1;

    public DphRuleSpec Spec { get; private set; } = null!;

    public string Code => Spec.Code;

    internal static DphRule Create(Guid tenantId, Guid contractId, DphRuleSpec spec, int version) =>
        new()
        {
            TenantId = tenantId,
            ContractId = contractId,
            Version = version,
            Spec = spec with { Code = spec.Code.Trim().ToUpperInvariant(), Name = spec.Name.Trim(), Region = Text.Normalise(spec.Region) },
        };
}

/// <summary>A diesel price used for one period of one rule, kept so the adjustment that was applied can always be reproduced.</summary>
[AuditIgnore]
public sealed class DphPeriodSnapshot : Entity, ITenantScoped
{
    private DphPeriodSnapshot()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public Guid RuleId { get; private set; }

    public string RuleCode { get; private set; } = null!;

    public int RuleVersion { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public decimal ReferencePrice { get; private set; }

    public decimal VariationPercent { get; private set; }

    public decimal AdjustmentPercent { get; private set; }

    public string CalculationVersion { get; private set; } = null!;

    public static DphPeriodSnapshot Create(Guid tenantId, DphRule rule, DateOnly periodStart, decimal referencePrice, decimal variationPercent, decimal adjustmentPercent, string calculationVersion) =>
        new()
        {
            TenantId = tenantId, ContractId = rule.ContractId, RuleId = rule.Id, RuleCode = rule.Code, RuleVersion = rule.Version, PeriodStart = periodStart, ReferencePrice = referencePrice,
            VariationPercent = variationPercent, AdjustmentPercent = adjustmentPercent, CalculationVersion = calculationVersion,
        };
}

/// <param name="Amount">The rupee adjustment to freight (negative for de-escalation).</param>
public sealed record DphResult(decimal AdjustmentPercent, decimal Amount, decimal VariationPercent, int Steps, decimal ReferencePrice, DateOnly ReferenceDate, string Explanation, bool Applied);

public static class DphCalculator
{
    /// <summary>The day whose diesel price counts, given how often the rule reprices.</summary>
    public static DateOnly ReferenceDate(DphFrequency frequency, DateOnly shipmentDate) => frequency switch
    {
        DphFrequency.Weekly => shipmentDate.AddDays(-(((int)shipmentDate.DayOfWeek + 6) % 7)),
        DphFrequency.Fortnightly => new DateOnly(shipmentDate.Year, shipmentDate.Month, shipmentDate.Day >= 16 ? 16 : 1),
        DphFrequency.Monthly => new DateOnly(shipmentDate.Year, shipmentDate.Month, 1),
        DphFrequency.Quarterly => new DateOnly(shipmentDate.Year, ((shipmentDate.Month - 1) / 3 * 3) + 1, 1),
        _ => shipmentDate,
    };

    /// <summary>The rule version in force on a date for a code (versions of one code must not overlap), or null.</summary>
    public static DphRule? InForce(IEnumerable<DphRule> rules, string? code, DateOnly date, DateOnly contractFrom, DateOnly contractTo)
    {
        var live = rules.Where(r => (r.Spec.EffectiveFrom ?? contractFrom) <= date && date <= (r.Spec.EffectiveTo ?? contractTo));
        if (!string.IsNullOrWhiteSpace(code))
        {
            return live.Where(r => string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Version).FirstOrDefault();
        }

        return live.Where(r => r.Spec.IsDefault).OrderByDescending(r => r.Version).FirstOrDefault();
    }

    /// <param name="currentPrice">The diesel price for the reference period.</param>
    public static DphResult Calculate(DphRuleSpec rule, decimal currentPrice, DateOnly referenceDate, decimal baseFreight, decimal? distanceKm)
    {
        var variation = Math.Round((currentPrice - rule.BaseDieselPrice) / rule.BaseDieselPrice * 100m, 4, MidpointRounding.AwayFromZero);
        var basis = $"diesel ₹{currentPrice:0.00} against base ₹{rule.BaseDieselPrice:0.00} ({rule.Region}, {referenceDate:dd MMM yyyy}) is {variation:+0.##;-0.##;0}%";

        DphResult None(string why) => new(0m, 0m, variation, 0, currentPrice, referenceDate, $"No DPH adjustment: {why}", false);

        if ((rule.Direction == DphDirection.EscalationOnly && variation < 0) || (rule.Direction == DphDirection.DeEscalationOnly && variation > 0))
        {
            return None($"{basis}; this rule only {(rule.Direction == DphDirection.EscalationOnly ? "escalates" : "de-escalates")}");
        }

        var sign = Math.Sign(variation);
        var size = Math.Abs(variation);

        if (rule.Formula != DphFormula.Indexed && size <= rule.ThresholdPercent)
        {
            return None($"{basis}, within the ±{rule.ThresholdPercent:0.##}% threshold");
        }

        decimal percent;
        decimal amount;
        var steps = 0;
        string how;
        switch (rule.Formula)
        {
            case DphFormula.PercentageVariation:
            {
                var counted = rule.OnExcessOnly ? size - rule.ThresholdPercent : size;
                percent = sign * counted * rule.FuelComponentPercent / 100m;
                how = $"{counted:0.##}% × fuel share {rule.FuelComponentPercent:0.##}%{(rule.OnExcessOnly ? " (only the part beyond the threshold)" : string.Empty)}";
                break;
            }

            case DphFormula.Indexed:
                percent = variation * rule.FuelComponentPercent / 100m;
                how = $"{variation:+0.##;-0.##}% × fuel share {rule.FuelComponentPercent:0.##}% (indexed, no threshold)";
                break;

            case DphFormula.ThresholdSteps:
                steps = (int)Math.Floor(size / rule.StepPercent);
                if (steps == 0)
                {
                    return None($"{basis}, less than one {rule.StepPercent:0.##}% step");
                }

                percent = sign * steps * rule.ImpactPercentPerStep;
                how = $"{steps} step(s) of {rule.StepPercent:0.##}% × {rule.ImpactPercentPerStep:0.##}% each";
                break;

            case DphFormula.FixedAdjustment:
                steps = (int)Math.Floor(size / rule.StepPercent);
                if (steps == 0)
                {
                    return None($"{basis}, less than one {rule.StepPercent:0.##}% step");
                }

                amount = FreightRound(sign * steps * rule.FixedAmountPerStep);
                percent = baseFreight > 0 ? Math.Round(amount / baseFreight * 100m, rule.AdjustmentDecimals, MidpointRounding.AwayFromZero) : 0m;
                return new DphResult(percent, amount, variation, steps, currentPrice, referenceDate, $"DPH {amount:+0.##;-0.##} ({steps} step(s) of {rule.StepPercent:0.##}% × ₹{rule.FixedAmountPerStep:0.##}; {basis})", true);

            default: // PerKmAdjustment
                steps = (int)Math.Floor(size / rule.StepPercent);
                if (steps == 0)
                {
                    return None($"{basis}, less than one {rule.StepPercent:0.##}% step");
                }

                if (distanceKm is not > 0)
                {
                    return None($"{basis}, but a per-km rule needs the distance");
                }

                amount = FreightRound(sign * steps * rule.PerKmPerStep * distanceKm.Value);
                percent = baseFreight > 0 ? Math.Round(amount / baseFreight * 100m, rule.AdjustmentDecimals, MidpointRounding.AwayFromZero) : 0m;
                return new DphResult(percent, amount, variation, steps, currentPrice, referenceDate, $"DPH {amount:+0.##;-0.##} ({steps} step(s) × ₹{rule.PerKmPerStep:0.##}/km × {distanceKm:0.##} km; {basis})", true);
        }

        var capped = false;
        if (rule.CapPercent is { } cap && Math.Abs(percent) > cap)
        {
            percent = Math.Sign(percent) * cap;
            capped = true;
        }

        percent = Math.Round(percent, rule.AdjustmentDecimals, MidpointRounding.AwayFromZero);
        amount = FreightRound(baseFreight * percent / 100m);
        return new DphResult(percent, amount, variation, steps, currentPrice, referenceDate, $"DPH {percent:+0.##;-0.##}% = {how}{(capped ? ", capped" : string.Empty)}; {basis}", true);
    }

    private static decimal FreightRound(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
