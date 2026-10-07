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
[JsonDerivedType(typeof(SlabRatePricing), "slabRate")]
public abstract record Pricing
{
    public abstract Result Validate();

    public abstract bool SuitsContractType(ContractType type);

    /// <summary>The service this pricing is for (a method, not a property, so it is not written into the stored JSON).</summary>
    public abstract ContractType ServiceType();

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

    public override ContractType ServiceType() => ContractType.Ftl;
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

    public override ContractType ServiceType() => ContractType.Ftl;
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

    public override ContractType ServiceType() => ContractType.Ptl;
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

    public override ContractType ServiceType() => ContractType.Dedicated;
}

/// <summary>What a slab is measured in. Bounds are always in the dimension's own base unit (kg, km, CBM, boxes).</summary>
public enum SlabDimension
{
    Weight = 1,
    Distance = 2,
    Volume = 3,
    Packages = 4,
}

/// <summary>How the slab table turns a quantity into money.</summary>
public enum SlabMethod
{
    /// <summary>The slab the whole quantity falls into sets the price of all of it.</summary>
    Flat = 1,

    /// <summary>Each slice of the quantity is charged at its own slab's rate.</summary>
    Progressive = 2,

    /// <summary>A fixed base amount covers the first slab; each slab beyond it charges only the excess quantity in it.</summary>
    BaseExcess = 3,
}

public enum SlabRateType
{
    /// <summary>The slab's rate is per unit of the pricing unit.</summary>
    PerUnit = 1,

    /// <summary>The slab's rate is one amount for the slab (a fixed price for the band).</summary>
    Fixed = 2,
}

/// <summary>The unit a per-unit rate is quoted in. Tonnes convert from kilograms by a fixed 1,000.</summary>
public enum RateUnit
{
    Kg = 1,
    Ton = 2,
    Km = 3,
    Cbm = 4,
    Box = 5,
    Trip = 6,
}

/// <summary>A band above <paramref name="From"/> up to and including <paramref name="To"/> (null = no upper limit).</summary>
public sealed record Slab(decimal From, decimal? To, decimal Rate, SlabRateType Type = SlabRateType.PerUnit);

/// <summary>
/// The general slab model: any dimension (weight, distance, volume, packages), any method, per-unit or fixed slab prices. The older weight-slab shape stays for
/// contracts already written with it; new rates should use this one.
/// </summary>
public sealed record SlabRatePricing(ContractType Service, SlabDimension Dimension, RateUnit Unit, SlabMethod Method, IReadOnlyList<Slab> Slabs, decimal MinChargeable = 0) : Pricing
{
    public const int MaxSlabs = 50;

    public override ContractType ServiceType() => Service;

    public override bool SuitsContractType(ContractType type) => type == Service;

    /// <summary>The factor between the dimension's base unit and the unit the rate is quoted in (1 kg = 0.001 ton).</summary>
    public decimal UnitDivisor => Unit == RateUnit.Ton ? 1000m : 1m;

    public override Result Validate()
    {
        if (!Enum.IsDefined(Service) || !Enum.IsDefined(Dimension) || !Enum.IsDefined(Method) || !Enum.IsDefined(Unit))
        {
            return Fail("service", "Choose the service, the slab dimension, the method and the unit.");
        }

        if (!UnitSuitsDimension())
        {
            return Fail("unit", $"{Unit} cannot price a {Dimension} slab.");
        }

        if (Slabs is not { Count: > 0 and <= MaxSlabs })
        {
            return Fail("slabs", $"Add between 1 and {MaxSlabs} slabs.");
        }

        if (MinChargeable < 0)
        {
            return Fail("minChargeable", "The minimum chargeable quantity cannot be negative.");
        }

        if (Slabs[0].From != 0)
        {
            return Fail("slabs", "The first slab must start at 0.");
        }

        for (var i = 0; i < Slabs.Count; i++)
        {
            var slab = Slabs[i];
            var label = $"Slab {i + 1}";
            if (!Enum.IsDefined(slab.Type) || slab.Rate <= 0)
            {
                return Fail("slabs", $"{label}: the rate must be more than zero.");
            }

            var last = i == Slabs.Count - 1;
            if (slab.To is null && !last)
            {
                return Fail("slabs", $"{label}: only the last slab can have no upper limit.");
            }

            if (slab.To is { } to && to <= slab.From)
            {
                return Fail("slabs", $"{label}: the upper limit must be above the lower limit.");
            }

            if (!last && Slabs[i + 1].From != slab.To)
            {
                return Fail("slabs", $"Slab {i + 2} must start where slab {i + 1} ends ({slab.To}): no gaps or overlaps.");
            }
        }

        if (Method == SlabMethod.BaseExcess && (Slabs[0].Type != SlabRateType.Fixed || Slabs[0].To is null || Slabs.Skip(1).Any(s => s.Type != SlabRateType.PerUnit)))
        {
            return Fail("slabs", "A base + excess table has a fixed price for the first slab (with an upper limit) and per-unit rates for the slabs after it.");
        }

        return Result.Success();
    }

    private bool UnitSuitsDimension() => Dimension switch
    {
        SlabDimension.Weight => Unit is RateUnit.Kg or RateUnit.Ton or RateUnit.Trip,
        SlabDimension.Distance => Unit is RateUnit.Km or RateUnit.Trip,
        SlabDimension.Volume => Unit is RateUnit.Cbm or RateUnit.Trip,
        SlabDimension.Packages => Unit is RateUnit.Box or RateUnit.Trip,
        _ => false,
    };

    /// <summary>The slab a quantity falls into: above the lower limit, up to and including the upper limit (a zero quantity falls into the first).</summary>
    public Slab? SlabFor(decimal quantity) =>
        Slabs.FirstOrDefault(s => (quantity > s.From || (quantity == 0 && s.From == 0)) && (s.To is null || quantity <= s.To));

    /// <summary>The price for a quantity (in the dimension's base unit) and a plain-words basis for the quote.</summary>
    public (decimal Amount, string Basis) Price(decimal quantity)
    {
        var unit = Unit.ToString().ToLowerInvariant();
        decimal PerUnitAmount(decimal baseUnits, decimal rate) => baseUnits / UnitDivisor * rate;

        switch (Method)
        {
            case SlabMethod.Flat:
            {
                var slab = SlabFor(quantity) ?? Slabs[^1];
                return slab.Type == SlabRateType.Fixed
                    ? (slab.Rate, $"₹{slab.Rate:0.##} fixed for the {Label(slab)} slab ({quantity:0.##} {BaseUnitName()})")
                    : (PerUnitAmount(quantity, slab.Rate), $"{quantity / UnitDivisor:0.##} {unit} × ₹{slab.Rate:0.####}/{unit} ({Label(slab)} slab)");
            }

            case SlabMethod.Progressive:
            {
                decimal total = 0;
                foreach (var slab in Slabs.Where(s => quantity > s.From))
                {
                    var upper = slab.To is { } to ? Math.Min(quantity, to) : quantity;
                    total += slab.Type == SlabRateType.Fixed ? slab.Rate : PerUnitAmount(upper - slab.From, slab.Rate);
                }

                return (total, $"{quantity / UnitDivisor:0.##} {unit} charged slab by slab");
            }

            default: // BaseExcess
            {
                var baseSlab = Slabs[0];
                decimal total = baseSlab.Rate;
                foreach (var slab in Slabs.Skip(1).Where(s => quantity > s.From))
                {
                    var upper = slab.To is { } to ? Math.Min(quantity, to) : quantity;
                    total += PerUnitAmount(upper - slab.From, slab.Rate);
                }

                return (total, quantity <= baseSlab.To
                    ? $"₹{baseSlab.Rate:0.##} base covers up to {baseSlab.To:0.##} {BaseUnitName()}"
                    : $"₹{baseSlab.Rate:0.##} base for the first {baseSlab.To:0.##} {BaseUnitName()} plus excess slab by slab");
            }
        }
    }

    private string BaseUnitName() => Dimension switch { SlabDimension.Weight => "kg", SlabDimension.Distance => "km", SlabDimension.Volume => "CBM", _ => "boxes" };

    private string Label(Slab slab) => slab.To is { } to ? $"{slab.From:0.##}–{to:0.##} {BaseUnitName()}" : $"above {slab.From:0.##} {BaseUnitName()}";
}
