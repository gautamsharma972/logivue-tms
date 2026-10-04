using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum FuelStepUnit
{
    /// <summary>Steps are measured as a percentage change in the diesel price against the base price.</summary>
    Percent = 1,

    /// <summary>Steps are measured in rupees per litre against the base price.</summary>
    Rupees = 2,
}

public enum FuelDirection
{
    /// <summary>Freight moves both up and down with diesel (escalation and de-escalation).</summary>
    Both = 1,

    /// <summary>Freight only goes up when diesel rises; it never falls below the base rate.</summary>
    EscalationOnly = 2,
}

/// <param name="Percent">Signed freight adjustment, e.g. +2.5 means freight is 2.5% higher.</param>
public sealed record FuelAdjustment(decimal Percent, int Steps, decimal Change, string Explanation);

/// <summary>
/// The diesel price variation (DPH) clause. For every <see cref="StepSize"/> that the diesel price moves away from
/// <see cref="BasePricePerLitre"/> (once past the dead band), base freight moves by <see cref="ImpactPercentPerStep"/>,
/// optionally capped. Whole steps only: a partial step does not count.
/// </summary>
public sealed record FuelClause(
    string Region,
    decimal BasePricePerLitre,
    FuelStepUnit Unit,
    decimal StepSize,
    decimal ImpactPercentPerStep,
    decimal DeadBand,
    decimal? CapPercent,
    FuelDirection Direction)
{
    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.fuel_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (string.IsNullOrWhiteSpace(Region))
        {
            return Fail("region", "Choose the diesel price region the clause refers to.");
        }

        if (BasePricePerLitre is <= 0 or > 500)
        {
            return Fail("basePricePerLitre", "Enter the base diesel price per litre.");
        }

        if (StepSize <= 0)
        {
            return Fail("stepSize", "The step size must be more than zero.");
        }

        if (ImpactPercentPerStep <= 0 || ImpactPercentPerStep > 100)
        {
            return Fail("impactPercentPerStep", "The freight change per step must be between 0 and 100%.");
        }

        if (DeadBand < 0)
        {
            return Fail("deadBand", "The dead band cannot be negative.");
        }

        return CapPercent is <= 0 or > 100 ? Fail("capPercent", "The cap must be between 0 and 100%.") : Result.Success();
    }

    public FuelAdjustment Evaluate(decimal currentPrice)
    {
        var rupeeChange = currentPrice - BasePricePerLitre;
        var change = Unit == FuelStepUnit.Percent ? rupeeChange / BasePricePerLitre * 100m : rupeeChange;
        var unit = Unit == FuelStepUnit.Percent ? "%" : " ₹";
        var summary = $"diesel ₹{currentPrice:0.00} vs base ₹{BasePricePerLitre:0.00} ({Region})";

        if (Direction == FuelDirection.EscalationOnly && change < 0)
        {
            return new FuelAdjustment(0m, 0, change, $"No fuel adjustment: {summary}; the clause only escalates");
        }

        if (Math.Abs(change) < DeadBand || Math.Abs(change) < StepSize)
        {
            return new FuelAdjustment(0m, 0, change, $"No fuel adjustment: {summary} is within the tolerance");
        }

        var steps = (int)Math.Floor(Math.Abs(change) / StepSize);
        var percent = Math.Sign(change) * steps * ImpactPercentPerStep;
        var capped = false;
        if (CapPercent is { } cap && Math.Abs(percent) > cap)
        {
            percent = Math.Sign(percent) * cap;
            capped = true;
        }

        percent = Math.Round(percent, 4, MidpointRounding.AwayFromZero);
        var note = $"Fuel adjustment {percent:+0.##;-0.##}% ({steps} × {ImpactPercentPerStep:0.##}% for {Math.Abs(change):0.##}{unit.Trim()} {(change > 0 ? "above" : "below")} base; {summary}){(capped ? ", capped" : string.Empty)}";
        return new FuelAdjustment(percent, steps, change, note);
    }
}

/// <summary>The pump price of diesel in a region from a date, entered by an administrator (or imported).</summary>
public sealed class DieselPrice : AggregateRoot, ITenantScoped
{
    private DieselPrice()
    {
    }

    public Guid TenantId { get; private set; }

    public string Region { get; private set; } = null!;

    public DateOnly EffectiveFrom { get; private set; }

    public decimal PricePerLitre { get; private set; }

    public static Result<DieselPrice> Create(Guid tenantId, string region, DateOnly effectiveFrom, decimal pricePerLitre)
    {
        if (string.IsNullOrWhiteSpace(region) || region.Length > 64)
        {
            return Error.Validation("diesel.region_invalid", "A region (e.g. Delhi, Mumbai, National) is required.");
        }

        if (pricePerLitre is <= 0 or > 500)
        {
            return Error.Validation("diesel.price_invalid", "Enter a realistic price per litre.");
        }

        return new DieselPrice { TenantId = tenantId, Region = Text.Normalise(region), EffectiveFrom = effectiveFrom, PricePerLitre = Math.Round(pricePerLitre, 2) };
    }

    /// <summary>The price in force on <paramref name="date"/>: the latest entry that started on or before it.</summary>
    public static decimal? Resolve(IEnumerable<DieselPrice> prices, string region, DateOnly date)
    {
        var normal = Text.Normalise(region);
        return prices.Where(p => p.Region == normal && p.EffectiveFrom <= date).MaxBy(p => p.EffectiveFrom)?.PricePerLitre;
    }
}
