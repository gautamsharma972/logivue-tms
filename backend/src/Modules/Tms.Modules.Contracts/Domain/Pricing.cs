using System.Text.Json.Serialization;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum ContractType
{
    /// <summary>Full truck load: the whole vehicle is hired for a lane.</summary>
    Ftl = 1,

    /// <summary>Part truck load: priced on the weight of a consignment.</summary>
    Ptl = 2,

    /// <summary>A vehicle reserved for the shipper for a period, billed monthly.</summary>
    Dedicated = 3,
}

public enum SlabMode
{
    /// <summary>The rate of the slab the whole chargeable weight falls into applies to all of it (common in Indian PTL rate sheets).</summary>
    Whole = 1,

    /// <summary>Each slice of weight is charged at its own slab's rate, like income-tax brackets.</summary>
    Incremental = 2,
}

/// <summary>Covers weights above <see cref="FromKg"/> up to and including <see cref="ToKg"/> (null = no upper limit).</summary>
public sealed record WeightSlab(decimal FromKg, decimal? ToKg, decimal RatePerKg);

/// <summary>How a rate card turns a shipment into money. A closed set of shapes, each validated on its own.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FlatTripPricing), "flatTrip")]
[JsonDerivedType(typeof(PerKmPricing), "perKm")]
[JsonDerivedType(typeof(WeightSlabPricing), "weightSlabs")]
[JsonDerivedType(typeof(DedicatedPricing), "dedicated")]
public abstract record Pricing
{
    public abstract Result Validate();

    public abstract bool SuitsContractType(ContractType type);

    protected static Result Fail(string field, string message) =>
        Error.Validation("contracts.pricing_invalid", message) with
        {
            ValidationErrors = new Dictionary<string, string[]> { [field] = [message] },
        };
}

/// <summary>A fixed price for the trip on this lane (and vehicle type).</summary>
public sealed record FlatTripPricing(decimal AmountPerTrip) : Pricing
{
    public override Result Validate() => AmountPerTrip > 0 ? Result.Success() : Fail("amountPerTrip", "The trip price must be more than zero.");

    public override bool SuitsContractType(ContractType type) => type == ContractType.Ftl;
}

/// <summary>Price per kilometre, with a minimum distance billed and a minimum charge.</summary>
public sealed record PerKmPricing(decimal RatePerKm, decimal MinKm, decimal MinCharge) : Pricing
{
    public override Result Validate() =>
        RatePerKm <= 0 ? Fail("ratePerKm", "The rate per km must be more than zero.")
        : MinKm < 0 ? Fail("minKm", "Minimum km cannot be negative.")
        : MinCharge < 0 ? Fail("minCharge", "The minimum charge cannot be negative.")
        : Result.Success();

    public override bool SuitsContractType(ContractType type) => type == ContractType.Ftl;
}

/// <summary>Part-load pricing by chargeable weight.</summary>
public sealed record WeightSlabPricing(SlabMode Mode, IReadOnlyList<WeightSlab> Slabs, decimal MinCharge, decimal MinChargeableKg) : Pricing
{
    public const int MaxSlabs = 30;

    public override Result Validate()
    {
        if (Slabs is not { Count: > 0 and <= MaxSlabs })
        {
            return Fail("slabs", $"Add between 1 and {MaxSlabs} weight slabs.");
        }

        if (MinCharge < 0 || MinChargeableKg < 0)
        {
            return Fail("minCharge", "Minimums cannot be negative.");
        }

        if (Slabs[0].FromKg != 0)
        {
            return Fail("slabs", "The first slab must start at 0 kg.");
        }

        for (var i = 0; i < Slabs.Count; i++)
        {
            var slab = Slabs[i];
            if (slab.RatePerKg <= 0)
            {
                return Fail("slabs", $"Slab {i + 1}: the rate per kg must be more than zero.");
            }

            var last = i == Slabs.Count - 1;
            if (slab.ToKg is null && !last)
            {
                return Fail("slabs", $"Slab {i + 1}: only the last slab can have no upper limit.");
            }

            if (slab.ToKg is { } to && to <= slab.FromKg)
            {
                return Fail("slabs", $"Slab {i + 1}: the upper limit must be above the lower limit.");
            }

            if (!last && Slabs[i + 1].FromKg != slab.ToKg)
            {
                return Fail("slabs", $"Slab {i + 2} must start where slab {i + 1} ends ({slab.ToKg} kg) — no gaps or overlaps.");
            }
        }

        return Result.Success();
    }

    public override bool SuitsContractType(ContractType type) => type == ContractType.Ptl;
}

/// <summary>Monthly rental for a reserved vehicle, with an allowance of kilometres and hours.</summary>
public sealed record DedicatedPricing(decimal MonthlyRental, decimal IncludedKmPerMonth, decimal ExtraKmRate, decimal IncludedHoursPerMonth, decimal ExtraHourRate) : Pricing
{
    public override Result Validate() =>
        MonthlyRental <= 0 ? Fail("monthlyRental", "The monthly rental must be more than zero.")
        : IncludedKmPerMonth < 0 || ExtraKmRate < 0 ? Fail("includedKmPerMonth", "Kilometre allowance and rate cannot be negative.")
        : IncludedHoursPerMonth < 0 || ExtraHourRate < 0 ? Fail("includedHoursPerMonth", "Hours allowance and rate cannot be negative.")
        : Result.Success();

    public override bool SuitsContractType(ContractType type) => type == ContractType.Dedicated;
}
