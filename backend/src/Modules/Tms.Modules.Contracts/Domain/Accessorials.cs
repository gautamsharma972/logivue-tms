using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>How a kind of extra charge is worked out.</summary>
public enum AccessorialCalc
{
    /// <summary>One amount when it applies.</summary>
    Fixed = 1,

    /// <summary>A rate for each unit beyond what is included.</summary>
    PerUnit = 2,

    /// <summary>Bands of quantity, each at its own rate (detention hours 3–5 at one rate, beyond 5 at another).</summary>
    Tiered = 3,

    /// <summary>A share of the base freight.</summary>
    PercentOfFreight = 4,

    /// <summary>A cost the carrier paid and passes on (toll, parking, permit): the amount is supplied, then bounded by the contract.</summary>
    Reimbursed = 5,
}

/// <summary>The catalogue of extra charges a tenant uses (detention, toll, night halt…). Data, not code: tenants add their own.</summary>
public sealed class AccessorialType : AggregateRoot, ITenantScoped
{
    private AccessorialType()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public AccessorialCalc Calc { get; private set; }

    /// <summary>What a unit is: HOUR, KM, STOP, NIGHT, TRIP, BOX…</summary>
    public string Unit { get; private set; } = null!;

    public bool IsActive { get; private set; } = true;

    public static Result<AccessorialType> Create(Guid tenantId, string code, string name, string? description, AccessorialCalc calc, string unit)
    {
        var normal = (code ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_');
        if (normal.Length is 0 or > 40 || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 20 || !Enum.IsDefined(calc))
        {
            return Error.Validation("accessorials.invalid", "A code, a name, a calculation and a unit are required.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["code"] = ["A code (up to 40 characters), a name, a calculation and a unit are required."] },
            };
        }

        return new AccessorialType { TenantId = tenantId, Code = normal, Name = name.Trim(), Description = description?.Trim(), Calc = calc, Unit = unit.Trim().ToUpperInvariant() };
    }

    public Result Update(string name, string? description, AccessorialCalc calc, string unit, bool active)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 20 || !Enum.IsDefined(calc))
        {
            return Error.Validation("accessorials.invalid", "A name, a calculation and a unit are required.");
        }

        Name = name.Trim();
        Description = description?.Trim();
        Calc = calc;
        Unit = unit.Trim().ToUpperInvariant();
        IsActive = active;
        return Result.Success();
    }

    /// <summary>The standard set, offered the first time a tenant needs it. Each can be changed or switched off.</summary>
    public static IReadOnlyList<(string Code, string Name, AccessorialCalc Calc, string Unit)> Standard { get; } =
    [
        ("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR"),
        ("WAITING", "Waiting", AccessorialCalc.Tiered, "HOUR"),
        ("ADDITIONAL_STOP", "Additional stop", AccessorialCalc.PerUnit, "STOP"),
        ("EXTRA_KM", "Extra kilometres", AccessorialCalc.PerUnit, "KM"),
        ("EXTRA_HOUR", "Extra hours", AccessorialCalc.PerUnit, "HOUR"),
        ("NIGHT_HALT", "Night halt", AccessorialCalc.PerUnit, "NIGHT"),
        ("TOLL", "Toll", AccessorialCalc.Reimbursed, "TRIP"),
        ("PARKING", "Parking", AccessorialCalc.Reimbursed, "TRIP"),
        ("PERMIT", "Permit", AccessorialCalc.Reimbursed, "TRIP"),
        ("LOADING", "Loading", AccessorialCalc.Fixed, "TRIP"),
        ("UNLOADING", "Unloading", AccessorialCalc.Fixed, "TRIP"),
        ("HANDLING", "Handling", AccessorialCalc.PerUnit, "BOX"),
        ("REDELIVERY", "Redelivery", AccessorialCalc.Fixed, "TRIP"),
        ("CANCELLATION", "Cancellation", AccessorialCalc.Fixed, "TRIP"),
        ("ODA", "Out of delivery area", AccessorialCalc.Fixed, "TRIP"),
        ("VEHICLE_RETENTION", "Vehicle retention", AccessorialCalc.PerUnit, "DAY"),
    ];
}

/// <summary>A band of quantity above <paramref name="From"/> up to and including <paramref name="To"/> (null = open), charged at <paramref name="Rate"/> per unit. Bounds count from zero, so included units are a tier at rate 0.</summary>
public sealed record AccessorialTier(decimal From, decimal? To, decimal Rate);

/// <summary>When a charge applies at all. Anything left empty does not restrict.</summary>
public sealed record AccessorialTrigger(
    IReadOnlyList<ContractType>? Services = null,
    decimal? MinWeightKg = null,
    decimal? MaxWeightKg = null,
    IReadOnlyList<string>? RequiredCapabilities = null,
    int? MinStops = null);

public sealed record AccessorialSpec(
    string Code,
    string Name,
    AccessorialCalc Calc,
    string Unit,
    decimal Rate = 0,
    decimal? MinimumCharge = null,
    decimal? MaximumCharge = null,
    decimal IncludedQuantity = 0,
    IReadOnlyList<AccessorialTier>? Tiers = null,
    AccessorialTrigger? Trigger = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    bool AutoApply = false)
{
    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.accessorial_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length > 40 || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100 || string.IsNullOrWhiteSpace(Unit) || Unit.Length > 20)
        {
            return Fail("code", "A code, a name and a unit are required.");
        }

        if (!Enum.IsDefined(Calc) || Rate < 0 || IncludedQuantity < 0 || MinimumCharge < 0 || MaximumCharge < 0 || (MinimumCharge is { } lo && MaximumCharge is { } hi && hi < lo))
        {
            return Fail("rate", "Rates and quantities cannot be negative, and the maximum cannot be below the minimum.");
        }

        if (ValidFrom is { } from && ValidTo is { } to && to < from)
        {
            return Fail("validTo", "The charge must end on or after the day it starts.");
        }

        switch (Calc)
        {
            case AccessorialCalc.Fixed or AccessorialCalc.PerUnit when Rate <= 0:
                return Fail("rate", "Give the rate.");
            case AccessorialCalc.PercentOfFreight when Rate is <= 0 or > 100:
                return Fail("rate", "The share of freight is between 0 and 100%.");
            case AccessorialCalc.Tiered:
                if (Tiers is not { Count: > 0 })
                {
                    return Fail("tiers", "Add at least one tier.");
                }

                for (var i = 0; i < Tiers.Count; i++)
                {
                    var t = Tiers[i];
                    if (t.Rate < 0 || (t.To is { } end && end <= t.From) || (t.To is null && i != Tiers.Count - 1) || (i > 0 && Tiers[i - 1].To != t.From) || (i == 0 && t.From != 0))
                    {
                        return Fail("tiers", $"Tier {i + 1}: tiers start at 0, follow each other with no gaps, and only the last is open.");
                    }
                }

                break;
        }

        return Trigger?.MinWeightKg < 0 || Trigger?.MaxWeightKg < 0 ? Fail("trigger", "Weights cannot be negative.") : Result.Success();
    }
}

/// <summary>An extra charge a contract allows, with its own rate and trigger.</summary>
public sealed class ContractAccessorial : Entity, ITenantScoped
{
    private ContractAccessorial()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public AccessorialSpec Spec { get; private set; } = null!;

    public string Code => Spec.Code;

    internal static ContractAccessorial Create(Guid tenantId, Guid contractId, AccessorialSpec spec) =>
        new() { TenantId = tenantId, ContractId = contractId, Spec = spec with { Code = spec.Code.Trim().ToUpperInvariant(), Name = spec.Name.Trim(), Unit = spec.Unit.Trim().ToUpperInvariant() } };
}

/// <summary>What the calculator needs to know about the shipment.</summary>
public sealed record AccessorialContext(
    DateOnly Date, ContractType Service, decimal WeightKg, int StopCount, IReadOnlySet<string> Capabilities, IReadOnlyDictionary<string, decimal> Inputs, decimal BaseFreight);

public sealed record AccessorialLine(string Code, string Description, decimal? Quantity, string Unit, decimal? Rate, decimal Amount);

public static class AccessorialCalculator
{
    /// <summary>The charge, or null when it does not apply (outside its dates or trigger, or nothing happened that earns it).</summary>
    public static AccessorialLine? Calculate(AccessorialSpec spec, AccessorialContext ctx)
    {
        if ((spec.ValidFrom is { } from && ctx.Date < from) || (spec.ValidTo is { } to && ctx.Date > to) || !Triggered(spec.Trigger, ctx))
        {
            return null;
        }

        // What happened: given by the caller (actuals or an estimate), except stops, which the shipment itself knows.
        var supplied = ctx.Inputs.TryGetValue(spec.Code, out var given);
        var quantity = supplied ? given : spec.Code == "ADDITIONAL_STOP" ? ctx.StopCount : spec.AutoApply ? 1m : 0m;
        if (quantity <= 0 && spec.Calc != AccessorialCalc.Fixed)
        {
            return null;
        }

        if (spec.Calc == AccessorialCalc.Fixed && quantity <= 0)
        {
            return null;
        }

        decimal amount;
        string how;
        switch (spec.Calc)
        {
            case AccessorialCalc.Fixed:
                amount = spec.Rate;
                how = "fixed charge";
                break;

            case AccessorialCalc.PerUnit:
            {
                var chargeable = Math.Max(0m, quantity - spec.IncludedQuantity);
                if (chargeable == 0)
                {
                    return null;
                }

                amount = chargeable * spec.Rate;
                how = $"{chargeable:0.##} {spec.Unit.ToLowerInvariant()}{(spec.IncludedQuantity > 0 ? $" beyond {spec.IncludedQuantity:0.##} included" : string.Empty)} × ₹{spec.Rate:0.##}";
                break;
            }

            case AccessorialCalc.Tiered:
            {
                decimal total = 0;
                var parts = new List<string>();
                foreach (var tier in spec.Tiers!.Where(t => quantity > t.From))
                {
                    var upper = tier.To is { } end ? Math.Min(quantity, end) : quantity;
                    var units = upper - tier.From;
                    total += units * tier.Rate;
                    parts.Add(tier.Rate == 0 ? $"{units:0.##} included" : $"{units:0.##} × ₹{tier.Rate:0.##}");
                }

                if (total == 0)
                {
                    return null;
                }

                amount = total;
                how = $"{quantity:0.##} {spec.Unit.ToLowerInvariant()}: {string.Join(", ", parts)}";
                break;
            }

            case AccessorialCalc.PercentOfFreight:
                amount = ctx.BaseFreight * spec.Rate / 100m;
                how = $"{spec.Rate:0.##}% of base freight";
                break;

            default: // Reimbursed
                amount = quantity;
                how = "passed through at cost";
                break;
        }

        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (spec.MinimumCharge is { } min && amount < min)
        {
            amount = min;
            how += $" (minimum ₹{min:0.##})";
        }

        if (spec.MaximumCharge is { } max && amount > max)
        {
            amount = max;
            how += $" (capped at ₹{max:0.##})";
        }

        return new AccessorialLine(spec.Code, $"{spec.Name}: {how}", quantity, spec.Unit, spec.Calc == AccessorialCalc.Fixed || spec.Calc == AccessorialCalc.PerUnit ? spec.Rate : null, amount);
    }

    private static bool Triggered(AccessorialTrigger? t, AccessorialContext ctx) =>
        t is null
        || ((t.Services is not { Count: > 0 } || t.Services.Contains(ctx.Service))
            && (t.MinWeightKg is null || ctx.WeightKg >= t.MinWeightKg)
            && (t.MaxWeightKg is null || ctx.WeightKg <= t.MaxWeightKg)
            && (t.MinStops is null || ctx.StopCount >= t.MinStops)
            && (t.RequiredCapabilities is not { Count: > 0 } || t.RequiredCapabilities.All(c => ctx.Capabilities.Contains(c.ToUpperInvariant()))));
}
