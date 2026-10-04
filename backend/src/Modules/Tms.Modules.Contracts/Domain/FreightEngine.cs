using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>Everything the rate engine needs to price a shipment.</summary>
public sealed record FreightQuery(
    DateOnly Date,
    Location Origin,
    Location Destination,
    Guid? VehicleTypeId,
    ContractType? Type,
    decimal? WeightKg,
    decimal? VolumeCbm,
    decimal? DistanceKm,
    int Drops = 1);

public sealed record QuoteLine(string Code, string Description, decimal Amount);

public sealed record FreightQuote(
    Guid ContractId,
    string ContractReference,
    Guid TransporterId,
    ContractType Type,
    Guid RateCardId,
    string Lane,
    decimal? ChargeableWeightKg,
    IReadOnlyList<QuoteLine> Lines,
    IReadOnlyList<string> Notes)
{
    public decimal Total => Lines.Sum(l => l.Amount);
}

/// <summary>Chooses which rate card of each contract applies to a shipment: the most specific one wins.</summary>
public static class RateSelector
{
    /// <param name="requireInForce">False to test a rate card before approval: status and dates are then ignored.</param>
    public static IReadOnlyList<(Contract Contract, RateCard Card)> BestPerContract(
        IEnumerable<Contract> contracts, FreightQuery query, Func<string, Zone?> zones, bool requireInForce = true)
    {
        var result = new List<(Contract, RateCard)>();
        foreach (var contract in contracts)
        {
            if ((requireInForce && !contract.IsInForce(query.Date)) || (query.Type is { } type && contract.Type != type))
            {
                continue;
            }

            RateCard? best = null;
            var bestScore = -1;
            foreach (var card in contract.RateCards)
            {
                if (!card.Applies(query, zones, out var score))
                {
                    continue;
                }

                // Ties are broken by id so the choice is stable between runs, not left to enumeration order.
                if (score > bestScore || (score == bestScore && best is not null && card.Id.CompareTo(best.Id) > 0))
                {
                    (best, bestScore) = (card, score);
                }
            }

            if (best is not null)
            {
                result.Add((contract, best));
            }
        }

        return result;
    }
}

/// <summary>
/// Turns a shipment plus an applicable rate into an itemised price. Pure: no database, no clock. Every amount is
/// rounded to paise (half away from zero) line by line, so the total is exactly the sum of what is shown.
/// </summary>
public static class FreightCalculator
{
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static Result<FreightQuote> Calculate(Contract contract, RateCard card, FreightQuery query, decimal? dieselPricePerLitre)
    {
        var terms = contract.Terms;
        var notes = new List<string>();
        decimal? chargeable = null;
        decimal baseAmount;
        string basis;

        switch (card.Pricing)
        {
            case FlatTripPricing flat:
                baseAmount = flat.AmountPerTrip;
                basis = "Flat rate for the trip";
                break;

            case PerKmPricing perKm:
                if (query.DistanceKm is not > 0)
                {
                    return Error.Validation("quote.distance_required", "The distance in km is needed to price a per-km rate.");
                }

                var km = Math.Max(query.DistanceKm.Value, perKm.MinKm);
                baseAmount = km * perKm.RatePerKm;
                basis = $"{km:0.##} km × ₹{perKm.RatePerKm:0.##}/km";
                if (km > query.DistanceKm.Value)
                {
                    notes.Add($"Billed for the minimum {perKm.MinKm:0.##} km");
                }

                if (baseAmount < perKm.MinCharge)
                {
                    baseAmount = perKm.MinCharge;
                    basis += $" (minimum charge ₹{perKm.MinCharge:0.##})";
                }

                break;

            case WeightSlabPricing slabs:
                if (query.WeightKg is not > 0)
                {
                    return Error.Validation("quote.weight_required", "The weight in kg is needed to price a part-load rate.");
                }

                var volumetric = (query.VolumeCbm ?? 0m) * terms.VolumetricKgPerCbm;
                chargeable = new[] { query.WeightKg.Value, volumetric, slabs.MinChargeableKg }.Max();
                if (volumetric > query.WeightKg.Value)
                {
                    notes.Add($"Volumetric weight {volumetric:0.##} kg ({query.VolumeCbm:0.##} CBM × {terms.VolumetricKgPerCbm:0.##}) is higher than actual {query.WeightKg:0.##} kg");
                }

                (baseAmount, basis) = PriceBySlab(slabs, chargeable.Value);
                var floor = new[] { slabs.MinCharge, terms.MinChargePerConsignment }.Max();
                if (baseAmount < floor)
                {
                    baseAmount = floor;
                    basis += $" (minimum charge ₹{floor:0.##})";
                }

                break;

            default:
                return Error.Validation("quote.dedicated_monthly", "Dedicated-vehicle contracts are billed monthly, not per shipment.");
        }

        var lines = new List<QuoteLine> { new("FREIGHT", basis, Round(baseAmount)) };

        if (contract.Fuel is { } clause)
        {
            if (dieselPricePerLitre is not { } price)
            {
                notes.Add($"No diesel price on file for {clause.Region}; no fuel adjustment applied");
            }
            else
            {
                var adjustment = clause.Evaluate(price);
                if (adjustment.Percent == 0)
                {
                    notes.Add(adjustment.Explanation);
                }
                else
                {
                    lines.Add(new QuoteLine("FUEL", adjustment.Explanation, Round(lines[0].Amount * adjustment.Percent / 100m)));
                }
            }
        }

        AddAccessorials(lines, terms, query.Drops);
        return new FreightQuote(contract.Id, contract.Reference, contract.TransporterId, contract.Type, card.Id, $"{card.Origin} → {card.Destination}", chargeable, lines, notes);
    }

    /// <summary>The monthly bill for a reserved vehicle: rental, plus charges for kilometres and hours beyond the allowance.</summary>
    public static Result<FreightQuote> CalculateDedicatedMonth(Contract contract, RateCard card, decimal kmRun, decimal hoursRun, decimal? dieselPricePerLitre)
    {
        if (card.Pricing is not DedicatedPricing d)
        {
            return Error.Validation("quote.not_dedicated", "This rate is not a dedicated-vehicle rate.");
        }

        if (kmRun < 0 || hoursRun < 0)
        {
            return Error.Validation("quote.usage_invalid", "Kilometres and hours cannot be negative.");
        }

        var notes = new List<string>();
        var lines = new List<QuoteLine> { new("RENTAL", "Monthly rental", Round(d.MonthlyRental)) };

        var extraKm = Math.Max(0m, kmRun - d.IncludedKmPerMonth);
        if (extraKm > 0 && d.ExtraKmRate > 0)
        {
            lines.Add(new QuoteLine("EXTRA_KM", $"{extraKm:0.##} km beyond the {d.IncludedKmPerMonth:0.##} km allowance × ₹{d.ExtraKmRate:0.##}", Round(extraKm * d.ExtraKmRate)));
        }

        var extraHours = Math.Max(0m, hoursRun - d.IncludedHoursPerMonth);
        if (extraHours > 0 && d.IncludedHoursPerMonth > 0 && d.ExtraHourRate > 0)
        {
            lines.Add(new QuoteLine("EXTRA_HOURS", $"{extraHours:0.##} hours beyond the {d.IncludedHoursPerMonth:0.##} hour allowance × ₹{d.ExtraHourRate:0.##}", Round(extraHours * d.ExtraHourRate)));
        }

        if (contract.Fuel is { } clause)
        {
            if (dieselPricePerLitre is not { } price)
            {
                notes.Add($"No diesel price on file for {clause.Region}; no fuel adjustment applied");
            }
            else
            {
                var adjustment = clause.Evaluate(price);
                if (adjustment.Percent == 0)
                {
                    notes.Add(adjustment.Explanation);
                }
                else
                {
                    lines.Add(new QuoteLine("FUEL", adjustment.Explanation, Round(lines[0].Amount * adjustment.Percent / 100m)));
                }
            }
        }

        return new FreightQuote(contract.Id, contract.Reference, contract.TransporterId, contract.Type, card.Id, $"{card.Origin} → {card.Destination}", null, lines, notes);
    }

    private static (decimal Amount, string Basis) PriceBySlab(WeightSlabPricing pricing, decimal chargeableKg)
    {
        if (pricing.Mode == SlabMode.Whole)
        {
            var slab = pricing.Slabs.First(s => chargeableKg > s.FromKg && (s.ToKg is null || chargeableKg <= s.ToKg));
            return (chargeableKg * slab.RatePerKg, $"{chargeableKg:0.##} kg × ₹{slab.RatePerKg:0.####}/kg ({SlabLabel(slab)})");
        }

        decimal total = 0;
        foreach (var slab in pricing.Slabs.Where(s => chargeableKg > s.FromKg))
        {
            var upper = slab.ToKg is { } to ? Math.Min(chargeableKg, to) : chargeableKg;
            total += (upper - slab.FromKg) * slab.RatePerKg;
        }

        return (total, $"{chargeableKg:0.##} kg charged slab by slab");
    }

    private static string SlabLabel(WeightSlab slab) =>
        slab.ToKg is { } to ? $"{slab.FromKg:0.##}–{to:0.##} kg slab" : $"above {slab.FromKg:0.##} kg slab";

    private static void AddAccessorials(List<QuoteLine> lines, ContractTerms terms, int drops)
    {
        if (terms.LoadingCharge > 0)
        {
            lines.Add(new QuoteLine("LOADING", "Loading charge", Round(terms.LoadingCharge)));
        }

        if (terms.UnloadingCharge > 0)
        {
            lines.Add(new QuoteLine("UNLOADING", "Unloading charge", Round(terms.UnloadingCharge)));
        }

        if (drops > 1 && terms.MultiDropChargePerPoint > 0)
        {
            lines.Add(new QuoteLine("MULTI_DROP", $"{drops - 1} extra drop point(s) × ₹{terms.MultiDropChargePerPoint:0.##}", Round((drops - 1) * terms.MultiDropChargePerPoint)));
        }
    }
}
