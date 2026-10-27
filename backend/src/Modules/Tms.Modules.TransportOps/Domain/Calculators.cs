using Tms.SharedKernel.Geo;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Domain;

public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Indian financial year (April to March) of a business date, as it appears in invoice numbers ("26-27").</summary>
public static class FinancialYear
{
    public static string Label(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{start % 100:00}-{(start + 1) % 100:00}";
    }

    public static (DateOnly From, DateOnly To) Range(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return (new DateOnly(start, 4, 1), new DateOnly(start + 1, 3, 31));
    }
}

public sealed record ArrivalDecision(ArrivalCheck Check, double? DistanceM, string Message)
{
    public bool Accepted => Check == ArrivalCheck.Inside;
}

/// <summary>
/// The one place the arrival decision is made. The phone supplies a position and the accuracy it reports; the server compares it with the destination's coordinates and radius. It is a
/// GPS signal from the driver's device, not proof of where the vehicle physically is.
/// </summary>
public static class GeofenceVerifier
{
    public static ArrivalDecision Verify(double? destinationLatitude, double? destinationLongitude, int radiusM, double? latitude, double? longitude, double? accuracyM, double maxAccuracyM)
    {
        if (destinationLatitude is null || destinationLongitude is null)
        {
            return new ArrivalDecision(ArrivalCheck.DestinationHasNoCoordinates, null, "This destination has no map coordinates yet, so arrival cannot be verified. Please contact the office.");
        }

        if (latitude is null || longitude is null || !double.IsFinite(latitude.Value) || !double.IsFinite(longitude.Value))
        {
            return new ArrivalDecision(ArrivalCheck.NoLocation, null, "Your location could not be read. Allow location access and try again.");
        }

        if (accuracyM is null || !double.IsFinite(accuracyM.Value) || accuracyM.Value < 0 || accuracyM.Value > maxAccuracyM)
        {
            return new ArrivalDecision(ArrivalCheck.AccuracyInsufficient, null, $"Your phone's location is not accurate enough (needs {maxAccuracyM:0} m or better). Move to an open area and try again.");
        }

        var distance = GeoMath.DistanceMetres(destinationLatitude.Value, destinationLongitude.Value, latitude.Value, longitude.Value);
        return distance <= radiusM
            ? new ArrivalDecision(ArrivalCheck.Inside, distance, "Arrival confirmed.")
            : new ArrivalDecision(ArrivalCheck.Outside, distance, $"You are about {Math.Round(distance):0} m from the delivery point; you need to be within {radiusM} m. Move closer and try again.");
    }
}

public enum DetentionStatus { NotApplicable, Calculated, OnHold }

/// <param name="HoldReason">Why detention could not be worked out. Detention is never assumed to be zero when a time is missing.</param>
public sealed record DetentionResult(
    DetentionStatus Status, string? HoldReason, DateTimeOffset? Arrival, DateTimeOffset? Unloading, double ElapsedHours, DateTimeOffset? FreeUntil, double ChargeableHours,
    decimal Units, DetentionUnit? ChargeUnit, decimal Rate, PartUnitRounding? Rounding, decimal? Cap, bool CapApplied, decimal Amount, IReadOnlyList<string> Trace);

/// <summary>Works out detention from the verified arrival and unloading times and the customer's own rule. Pure: the same inputs always give the same answer.</summary>
public static class DetentionCalculator
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static DetentionResult Calculate(DetentionPolicy policy, DateTimeOffset? arrival, DateTimeOffset? unloading)
    {
        List<string> trace = [];
        DetentionResult Hold(string reason) => new(DetentionStatus.OnHold, reason, arrival, unloading, 0, null, 0, 0, policy.ChargeUnit, policy.Rate, policy.Rounding, policy.MaxCharge, false, 0, [.. trace, reason]);

        if (!policy.Applicable)
        {
            trace.Add("Detention does not apply to this customer.");
            return new DetentionResult(DetentionStatus.NotApplicable, null, arrival, unloading, 0, null, 0, 0, null, 0, null, null, false, 0, trace);
        }

        if (arrival is null)
        {
            return Hold("The destination arrival time is missing, so detention cannot be worked out.");
        }

        if (unloading is null)
        {
            return Hold("The unloading completion time is missing, so detention cannot be worked out.");
        }

        if (unloading < arrival)
        {
            return Hold("Unloading completion is earlier than arrival: the times need to be corrected.");
        }

        var elapsed = unloading.Value - arrival.Value;
        trace.Add($"Waited {elapsed.TotalHours:0.##} h at the destination ({arrival:dd MMM HH:mm} to {unloading:dd MMM HH:mm}).");

        var freeUntil = policy.FreeTime == FreeTimeKind.EndOfDay ? EndOfDay(arrival.Value) + Free(policy) : arrival.Value + Free(policy);
        trace.Add(policy.FreeTime == FreeTimeKind.EndOfDay
            ? $"Free until the end of the arrival day plus {policy.FreeAmount:0.##} {policy.FreeUnit.ToString().ToLowerInvariant()}(s): {freeUntil.ToOffset(Ist):dd MMM HH:mm} IST."
            : $"Free for {policy.FreeAmount:0.##} {policy.FreeUnit.ToString().ToLowerInvariant()}(s): until {freeUntil.ToOffset(Ist):dd MMM HH:mm} IST.");

        var chargeable = unloading.Value > freeUntil ? unloading.Value - freeUntil : TimeSpan.Zero;
        if (chargeable == TimeSpan.Zero)
        {
            trace.Add("Unloading finished within the free time: no detention.");
            return new DetentionResult(DetentionStatus.Calculated, null, arrival, unloading, elapsed.TotalHours, freeUntil, 0, 0, policy.ChargeUnit, policy.Rate, policy.Rounding, policy.MaxCharge, false, 0, trace);
        }

        var raw = policy.ChargeUnit == DetentionUnit.Hour ? (decimal)chargeable.TotalHours : (decimal)chargeable.TotalDays;
        var units = policy.Rounding switch
        {
            PartUnitRounding.RoundUp => Math.Ceiling(raw),
            PartUnitRounding.RoundDown => Math.Floor(raw),
            PartUnitRounding.Nearest => Math.Round(raw, 0, MidpointRounding.AwayFromZero),
            _ => Math.Round(raw, 2, MidpointRounding.AwayFromZero),
        };
        trace.Add($"{raw:0.##} {policy.ChargeUnit.ToString().ToLowerInvariant()}(s) over the free time, charged as {units:0.##} ({policy.Rounding}).");

        var amount = Money.Round(units * policy.Rate);
        var capped = false;
        if (policy.MaxCharge is { } cap && amount > cap)
        {
            amount = cap;
            capped = true;
            trace.Add($"Limited to the agreed maximum of {cap:0.00}.");
        }

        trace.Add($"Detention {amount:0.00} ({units:0.##} x {policy.Rate:0.00}).");
        return new DetentionResult(DetentionStatus.Calculated, null, arrival, unloading, elapsed.TotalHours, freeUntil, chargeable.TotalHours, units, policy.ChargeUnit, policy.Rate, policy.Rounding, policy.MaxCharge, capped, amount, trace);
    }

    private static TimeSpan Free(DetentionPolicy p) => p.FreeUnit == DetentionUnit.Hour ? TimeSpan.FromHours((double)p.FreeAmount) : TimeSpan.FromDays((double)p.FreeAmount);

    /// <summary>The end of the arrival day in India (the following midnight, IST).</summary>
    private static DateTimeOffset EndOfDay(DateTimeOffset arrival)
    {
        var local = arrival.ToOffset(Ist);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Ist).AddDays(1);
    }
}

public sealed record FreightLine(string Code, string Description, decimal Amount, string Basis);

public sealed record LrFacts(DateOnly Date, Guid? OriginLocationId, string? OriginCity, string? OriginState, Guid? DestinationLocationId, string? DestinationCity, string? DestinationState, string? Material,
    decimal ActualWeightKg, decimal? VolumeCbm);

public sealed record FreightResult(Guid? RateId, decimal BillableWeightKg, string WeightBasisUsed, decimal BasicFreight, decimal OtherCharges, decimal Total, IReadOnlyList<FreightLine> Lines, IReadOnlyList<string> Trace);

/// <summary>Prices one consignment from a customer's agreement. Pure and deterministic: it never reads the clock or the database.</summary>
public static class CustomerFreightCalculator
{
    public static Result<FreightResult> Calculate(CustomerAgreement agreement, LrFacts facts)
    {
        if (!agreement.IsInForce(facts.Date))
        {
            return Error.NotFound("OPS_NO_AGREEMENT", $"There is no agreement in force on {facts.Date:dd MMM yyyy}.");
        }

        List<string> trace = [$"Agreement {agreement.Number} revision {agreement.Revision} ({agreement.EffectiveFrom:dd MMM yyyy} onwards)."];
        var scored = agreement.Rates.Select(r => (Rate: r, Score: Score(r, facts))).Where(x => x.Score >= 0).ToList();
        if (scored.Count == 0)
        {
            return Error.NotFound("OPS_RATE_NOT_FOUND", "No rate on the agreement covers this lane and material.");
        }

        var best = scored.Max(x => x.Score);
        var winners = scored.Where(x => x.Score == best).ToList();
        if (winners.Count > 1)
        {
            return Error.Conflict("OPS_RATE_CONFLICT", $"{winners.Count} rates match this lane equally well, so none can be chosen. Fix the agreement so that only one rate applies.");
        }

        var rate = winners[0].Rate;
        trace.Add($"Rate chosen by lane specificity (score {best}): {Describe(rate)}.");

        var (weight, basis, weightError) = BillableWeight(agreement, facts, trace);
        if (weightError is not null)
        {
            return weightError;
        }

        var baseAmount = rate.Method switch
        {
            RateMethod.PerKg => weight * rate.Rate,
            RateMethod.PerTonne => weight / 1000m * rate.Rate,
            RateMethod.FlatPerConsignment => rate.Rate,
            _ => SlabAmount(rate, weight, trace),
        };
        if (baseAmount is null)
        {
            return Error.NotFound("OPS_RATE_NOT_FOUND", $"No weight band of the rate covers {weight:0.##} kg.");
        }

        List<FreightLine> lines = [new("BASE_FREIGHT", "Basic freight", Money.Round(baseAmount.Value), Basis(rate, weight))];
        if (rate.MinimumCharge is { } rateMin && lines[0].Amount < rateMin)
        {
            lines.Add(new FreightLine("RATE_MINIMUM", "Raised to the rate's minimum charge", Money.Round(rateMin - lines[0].Amount), $"minimum {rateMin:0.00}"));
            trace.Add($"Rate minimum {rateMin:0.00} applied.");
        }

        var freight = lines.Sum(l => l.Amount);
        if (agreement.MinimumFreight is { } min && freight < min)
        {
            lines.Add(new FreightLine("MIN_FREIGHT", "Raised to the agreement's minimum freight", Money.Round(min - freight), $"minimum {min:0.00}"));
            trace.Add($"Agreement minimum freight {min:0.00} applied.");
            freight = lines.Sum(l => l.Amount);
        }

        var other = 0m;
        foreach (var charge in agreement.ExtraCharges)
        {
            var amount = Money.Round(charge.Amount);
            lines.Add(new FreightLine(charge.Code, charge.Name, amount, "agreed charge"));
            other += amount;
        }

        trace.Add($"Basic freight {freight:0.00}; other charges {other:0.00}.");
        return new FreightResult(rate.Id, weight, basis, freight, other, freight + other, lines, trace);
    }

    private static (decimal Weight, string Basis, Error? Error) BillableWeight(CustomerAgreement a, LrFacts f, List<string> trace)
    {
        var volumetric = f.VolumeCbm is { } v && a.VolumetricKgPerCbm is { } k ? v * k : (decimal?)null;
        switch (a.WeightBasis)
        {
            case WeightBasis.Actual:
                trace.Add($"Billable weight is the actual weight: {f.ActualWeightKg:0.##} kg.");
                return (f.ActualWeightKg, "Actual", null);
            case WeightBasis.Volumetric:
                if (volumetric is null)
                {
                    return (0, "Volumetric", Error.Validation("OPS_INPUT_MISSING", "This agreement bills on volumetric weight, but the LR has no volume or the agreement has no volumetric factor."));
                }

                trace.Add($"Billable weight is the volumetric weight: {f.VolumeCbm:0.##} CBM x {a.VolumetricKgPerCbm:0.##} = {volumetric:0.##} kg.");
                return (volumetric.Value, "Volumetric", null);
            default:
                if (volumetric is null)
                {
                    trace.Add($"No volume available, so the actual weight {f.ActualWeightKg:0.##} kg is used.");
                    return (f.ActualWeightKg, "Actual", null);
                }

                var higher = Math.Max(f.ActualWeightKg, volumetric.Value);
                trace.Add($"Billable weight is the higher of actual {f.ActualWeightKg:0.##} kg and volumetric {volumetric:0.##} kg: {higher:0.##} kg.");
                return (higher, volumetric.Value > f.ActualWeightKg ? "Volumetric" : "Actual", null);
        }
    }

    private static decimal? SlabAmount(AgreementRate rate, decimal weight, List<string> trace)
    {
        var slab = rate.Slabs.FirstOrDefault(s => weight > s.FromKg && (s.ToKg is null || weight <= s.ToKg));
        if (slab is null)
        {
            return null;
        }

        var upper = slab.ToKg is { } to ? $"{to:0.##}" : "above";
        trace.Add($"Weight band {slab.FromKg:0.##} to {upper} kg at {slab.RatePerKg:0.##} per kg, on the whole weight.");
        return weight * slab.RatePerKg;
    }

    /// <summary>-1 when the rate does not apply; otherwise how specific the match is (location 3, city 2, state 1, anywhere 0 for each end, plus 1 for a matching material).</summary>
    private static int Score(AgreementRate r, LrFacts f)
    {
        var origin = End(r.OriginLocationId, r.OriginCity, r.OriginState, f.OriginLocationId, f.OriginCity, f.OriginState);
        var destination = End(r.DestinationLocationId, r.DestinationCity, r.DestinationState, f.DestinationLocationId, f.DestinationCity, f.DestinationState);
        if (origin < 0 || destination < 0)
        {
            return -1;
        }

        var material = 0;
        if (r.Material is not null)
        {
            if (!string.Equals(r.Material, AgreementRate.Norm(f.Material), StringComparison.Ordinal))
            {
                return -1;
            }

            material = 1;
        }

        return origin + destination + material;
    }

    private static int End(Guid? rateLocation, string? rateCity, string? rateState, Guid? location, string? city, string? state)
    {
        if (rateLocation is not null)
        {
            return rateLocation == location ? 3 : -1;
        }

        if (rateCity is not null)
        {
            return rateCity == AgreementRate.Norm(city) && (rateState is null || rateState == AgreementRate.Norm(state)) ? 2 : -1;
        }

        if (rateState is not null)
        {
            return rateState == AgreementRate.Norm(state) ? 1 : -1;
        }

        return 0;
    }

    private static string Describe(AgreementRate r) => r.Method switch
    {
        RateMethod.PerKg => $"{r.Rate:0.####} per kg",
        RateMethod.PerTonne => $"{r.Rate:0.####} per tonne",
        RateMethod.FlatPerConsignment => $"{r.Rate:0.00} per consignment",
        _ => $"weight bands ({r.Slabs.Count})",
    };

    private static string Basis(AgreementRate r, decimal weight) => r.Method switch
    {
        RateMethod.PerKg => $"{weight:0.##} kg x {r.Rate:0.####}",
        RateMethod.PerTonne => $"{weight / 1000m:0.###} t x {r.Rate:0.####}",
        RateMethod.FlatPerConsignment => "flat per consignment",
        _ => $"{weight:0.##} kg, weight band",
    };
}

public sealed record TaxResult(TaxMode Mode, decimal RatePercent, decimal Cgst, decimal Sgst, decimal Igst, string? Note)
{
    public decimal Total => Cgst + Sgst + Igst;
}

/// <summary>Works out tax from the customer's agreement. Within the same state it is CGST + SGST; across states it is IGST. Nothing is assumed when the states are not known.</summary>
public static class TaxCalculator
{
    public static Result<TaxResult> Compute(decimal taxable, TaxSettings tax, string? supplierStateCode, string? customerStateCode)
    {
        switch (tax.Mode)
        {
            case TaxMode.None:
                return new TaxResult(TaxMode.None, 0, 0, 0, 0, tax.Note);
            case TaxMode.ReverseCharge:
                return new TaxResult(TaxMode.ReverseCharge, tax.RatePercent, 0, 0, 0, tax.Note ?? "Tax is payable by the recipient under reverse charge.");
        }

        if (tax.RatePercent <= 0)
        {
            return Error.Validation("OPS_TAX_RATE", "Tax is charged on this agreement, but no tax rate is set.");
        }

        if (string.IsNullOrWhiteSpace(supplierStateCode) || string.IsNullOrWhiteSpace(customerStateCode))
        {
            return Error.Validation("OPS_TAX_PLACE_OF_SUPPLY", "Tax cannot be worked out: the supplier state code (settings) and the customer's GSTIN are needed to decide between IGST and CGST + SGST.");
        }

        if (supplierStateCode == customerStateCode)
        {
            var half = Money.Round(taxable * tax.RatePercent / 200m);
            return new TaxResult(TaxMode.Forward, tax.RatePercent, half, half, 0, tax.Note);
        }

        return new TaxResult(TaxMode.Forward, tax.RatePercent, 0, 0, Money.Round(taxable * tax.RatePercent / 100m), tax.Note);
    }
}
