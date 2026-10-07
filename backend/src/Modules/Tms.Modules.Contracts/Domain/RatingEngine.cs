using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>What is asked of the engine. Capabilities are the special handling a shipment needs (HAZMAT, TEMP_CONTROLLED…).</summary>
public sealed record RatingInput(
    DateOnly Date,
    Guid? TransporterId,
    Location Origin,
    Location Destination,
    ContractType Service,
    Guid? VehicleTypeId,
    decimal? WeightKg,
    decimal? VolumeCbm,
    decimal? DistanceKm,
    int StopCount,
    IReadOnlyList<string> Capabilities,
    IReadOnlyDictionary<string, decimal> AccessorialInputs,
    string? ShipmentReference = null,
    decimal? Packages = null);

/// <param name="Snapshots">Diesel prices already recorded for a rule period; used in preference to the live index so a past adjustment is the one that was applied.</param>
/// <param name="RequireInForce">False to rate against a draft (preview): status and dates of the contract are then ignored.</param>
public sealed record RatingContext(
    Func<string, Zone?> Zones,
    IReadOnlyList<DieselPrice> Diesel,
    IReadOnlyDictionary<(Guid RuleId, DateOnly Period), DphPeriodSnapshot>? Snapshots = null,
    bool RequireInForce = true);

public sealed record RatingLine(string Type, string Description, decimal? Quantity, string? Unit, decimal? Rate, decimal Amount, string? Reference);

public sealed record RatingBreakdown(
    Contract Contract,
    RateCard Card,
    IReadOnlyList<RatingLine> Lines,
    decimal BaseFreight,
    decimal Dph,
    decimal Accessorials,
    decimal Discount,
    decimal Total,
    DphRule? DphRule,
    DphResult? DphResult,
    string Currency,
    IReadOnlyList<string> Notes,
    decimal? ChargeableWeightKg,
    IReadOnlyList<string> Reasons)
{
    /// <summary>The rate's preference (lower is preferred) and the specificity used to rank it.</summary>
    public int Priority => Card.Priority;
}

public sealed record RatingExclusionInfo(string ContractReference, string? RateCode, string Code, string Reason);

public sealed record TraceStep(string Stage, string Text, bool Ok = true);

public sealed record RatingOutcome(
    bool Qualified,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<RatingBreakdown> Options,
    IReadOnlyList<RatingExclusionInfo> Exclusions,
    IReadOnlyList<TraceStep> Trace,
    IReadOnlyList<string> Advice)
{
    public RatingBreakdown? Selected => Qualified && Options.Count > 0 ? Options[0] : null;
}

/// <summary>
/// The contract rating engine. Given the contracts that might apply, it decides which rate applies to a shipment and why, calculates the freight, and explains both. Pure: no
/// database, no clock. The same inputs always give the same answer, and nothing is decided by the order rows come back in.
/// </summary>
public static class RatingEngine
{
    /// <summary>Changes only when the way freight is calculated changes. Stored with every rating, so an old rating is always judged by the rules it was made under.</summary>
    public const string Version = "1.0";

    public static RatingOutcome Rate(IEnumerable<Contract> contracts, RatingInput input, RatingContext context)
    {
        var trace = new List<TraceStep>();
        var exclusions = new List<RatingExclusionInfo>();
        var conflicts = new List<string>();
        var winners = new List<(Contract Contract, RateCard Card, Candidate Key)>();
        var inputProblems = new List<string>();
        var sawLaneRate = false;

        var vehicle = input.VehicleTypeId is null ? "any vehicle" : "the requested vehicle";
        trace.Add(new TraceStep("Input", $"{Lane(input.Origin, input.Destination)} · {input.Service} · {vehicle} · {Num(input.WeightKg, "kg")} · {Num(input.DistanceKm, "km")} · {Num(input.VolumeCbm, "CBM")} · {input.StopCount} stop(s) · {input.Date:dd MMM yyyy}"));

        foreach (var contract in contracts.OrderBy(c => c.Number, StringComparer.Ordinal).ThenBy(c => c.Revision))
        {
            if (ContractExclusion(contract, input, context) is { } why)
            {
                exclusions.Add(new RatingExclusionInfo(contract.Reference, null, why.Code, why.Reason));
                trace.Add(new TraceStep("Contract", $"{contract.Reference}: {why.Reason}", false));
                continue;
            }

            trace.Add(new TraceStep("Contract", $"{contract.Reference}: in force on {input.Date:dd MMM yyyy}, covers {string.Join("/", contract.EffectiveServices)}"));

            var onLane = new List<(RateCard Card, int Geo)>();
            var otherLanes = 0;
            foreach (var card in contract.RateCards.OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                if (LaneScore(card, input, context.Zones) is { } geo)
                {
                    onLane.Add((card, geo));
                }
                else
                {
                    otherLanes++;
                }
            }

            sawLaneRate |= onLane.Count > 0;
            if (otherLanes > 0)
            {
                trace.Add(new TraceStep("Qualification", $"{contract.Reference}: {otherLanes} rate(s) are for other lanes and were not considered"));
            }

            var survivors = new List<(RateCard Card, Candidate Key)>();
            foreach (var (card, geo) in onLane)
            {
                if (RateExclusion(card, input) is { } excluded)
                {
                    exclusions.Add(new RatingExclusionInfo(contract.Reference, $"{card.Code} V{card.Version}", excluded.Code, excluded.Reason));
                    trace.Add(new TraceStep("Qualification", $"{card.Code} V{card.Version}: {excluded.Reason}", false));
                    if (excluded.Code.EndsWith("_REQUIRED", StringComparison.Ordinal))
                    {
                        inputProblems.Add(excluded.Reason);
                    }

                    continue;
                }

                survivors.Add((card, new Candidate(geo, card.VehicleTypeId is null ? 0 : 1, card.RequiredCapabilities.Count, Bands(card), card.Priority)));
            }

            if (survivors.Count == 0)
            {
                continue;
            }

            var best = survivors.OrderByDescending(s => s.Key.Geo).ThenByDescending(s => s.Key.Vehicle).ThenByDescending(s => s.Key.Capabilities).ThenByDescending(s => s.Key.Bands).ThenBy(s => s.Key.Priority).First().Key;
            var tied = survivors.Where(s => s.Key == best).ToList();
            if (tied.Count > 1)
            {
                var names = string.Join(", ", tied.Select(t => $"{t.Card.Code} V{t.Card.Version}"));
                conflicts.Add($"{contract.Reference}: {names} all qualify with the same specificity and priority");
                foreach (var (card, _) in tied)
                {
                    exclusions.Add(new RatingExclusionInfo(contract.Reference, $"{card.Code} V{card.Version}", "RATE_CONFLICT", $"Rate conflict: qualifies together with {tied.Count - 1} other rate(s) with the same lane, vehicle, slab and priority"));
                }

                trace.Add(new TraceStep("Selection", $"{contract.Reference}: RATE CONFLICT between {names}", false));
                continue;
            }

            var winner = tied[0];
            foreach (var (card, key) in survivors.Where(s => s.Card != winner.Card))
            {
                exclusions.Add(new RatingExclusionInfo(contract.Reference, $"{card.Code} V{card.Version}", "LOWER_PRIORITY", WhyLoser(key, best)));
            }

            winners.Add((contract, winner.Card, winner.Key));
        }

        // One contract per transporter: the most specific wins, and two equally good ones are a conflict rather than a coin toss.
        var perTransporter = new List<(Contract Contract, RateCard Card, Candidate Key)>();
        foreach (var group in winners.GroupBy(w => w.Contract.TransporterId))
        {
            var best = group.OrderByDescending(w => w.Key.Geo).ThenByDescending(w => w.Key.Vehicle).ThenByDescending(w => w.Key.Capabilities).ThenByDescending(w => w.Key.Bands).ThenBy(w => w.Key.Priority).First();
            var tied = group.Where(w => w.Key == best.Key).ToList();
            if (tied.Count > 1)
            {
                conflicts.Add($"{string.Join(" and ", tied.Select(t => t.Contract.Reference))} (same transporter) both have an equally good rate for this shipment");
                foreach (var t in tied)
                {
                    exclusions.Add(new RatingExclusionInfo(t.Contract.Reference, $"{t.Card.Code} V{t.Card.Version}", "CONTRACT_CONFLICT", "Another contract of the same transporter has an equally specific rate with the same priority"));
                }

                continue;
            }

            foreach (var other in group.Where(w => w.Contract != best.Contract))
            {
                exclusions.Add(new RatingExclusionInfo(other.Contract.Reference, $"{other.Card.Code} V{other.Card.Version}", "OTHER_CONTRACT_PREFERRED", $"{best.Contract.Reference} has a more specific or preferred rate for the same transporter"));
            }

            perTransporter.Add(best);
        }

        var options = new List<RatingBreakdown>();
        foreach (var (contract, card, _) in perTransporter)
        {
            var calculated = Calculate(contract, card, input, context, trace);
            if (calculated.IsFailure)
            {
                exclusions.Add(new RatingExclusionInfo(contract.Reference, $"{card.Code} V{card.Version}", "INPUT_MISSING", calculated.Error.Description));
                inputProblems.Add(calculated.Error.Description);
                trace.Add(new TraceStep("Calculation", $"{card.Code} V{card.Version}: {calculated.Error.Description}", false));
                continue;
            }

            options.Add(calculated.Value);
        }

        var ranked = options.OrderBy(o => o.Priority).ThenBy(o => o.Total).ThenBy(o => o.Contract.Number, StringComparer.Ordinal).ToList();
        if (ranked.Count > 0)
        {
            var chosen = ranked[0];
            if (ranked.Count > 1)
            {
                trace.Add(new TraceStep("Selection", $"{ranked.Count} transporters qualify; {chosen.Contract.Reference} is first (preference {chosen.Priority}, then lowest freight ₹{chosen.Total:0.##})"));
            }

            trace.Add(new TraceStep("Result", $"{chosen.Contract.Reference} · {chosen.Card.Code} V{chosen.Card.Version} · total {chosen.Currency} {chosen.Total:0.##}"));
            var extra = conflicts.Count > 0 ? new[] { $"Note: {conflicts.Count} other contract(s) could not be ranked because of a rate conflict" } : [];
            return new RatingOutcome(true, null, null, ranked, exclusions, trace, extra);
        }

        var lane = $"{Lane(input.Origin, input.Destination)}, {input.Service}, {Num(input.WeightKg, "kg")}, {Num(input.DistanceKm, "km")}, {input.Date:dd MMM yyyy}";
        if (conflicts.Count > 0)
        {
            trace.Add(new TraceStep("Result", "Rate conflict: no freight can be rated until it is resolved", false));
            return new RatingOutcome(false, "FREIGHT_RATE_CONFLICT", $"More than one rate qualifies and nothing decides between them: {string.Join("; ", conflicts)}.", [], exclusions, trace,
                ["Give one of the rates a different priority", "Change a slab or validity so they do not overlap", "Pick the approved rate explicitly"]);
        }

        if (inputProblems.Count > 0 && ranked.Count == 0 && sawLaneRate)
        {
            trace.Add(new TraceStep("Result", "Missing input", false));
            return new RatingOutcome(false, "FREIGHT_INPUT_MISSING", inputProblems[0], [], exclusions, trace, ["Supply the missing weight, distance or volume and rate again"]);
        }

        trace.Add(new TraceStep("Result", $"No applicable active rate for {lane}", false));
        return new RatingOutcome(false, "FREIGHT_RATE_NOT_FOUND", $"No applicable active freight rate was found for {lane}.", [], exclusions, trace,
            ["Check the lane rate and the zone rate", "Check the contract's validity", "Check the vehicle type", "Check the weight slab", "Check the distance slab"]);
    }

    // ---- qualification

    private sealed record Candidate(int Geo, int Vehicle, int Capabilities, int Bands, int Priority);

    private static (string Code, string Reason)? ContractExclusion(Contract contract, RatingInput input, RatingContext context)
    {
        if (input.TransporterId is { } wanted && contract.TransporterId != wanted)
        {
            return ("TRANSPORTER_MISMATCH", "belongs to another transporter");
        }

        if (!contract.EffectiveServices.Contains(input.Service))
        {
            return ("SERVICE_NOT_COVERED", $"does not cover {input.Service}");
        }

        if (!context.RequireInForce)
        {
            return null;
        }

        if (contract.IsInForce(input.Date))
        {
            return null;
        }

        return contract.Status switch
        {
            ContractStatus.Draft or ContractStatus.PendingApproval or ContractStatus.Rejected or ContractStatus.Cancelled => ("CONTRACT_NOT_ACTIVE", $"is {contract.Status}, not active"),
            _ when contract.Suspensions.Any(s => s.Covers(input.Date)) => ("CONTRACT_SUSPENDED", "was suspended on the shipment date"),
            _ when input.Date < contract.EffectiveFrom => ("CONTRACT_NOT_YET_EFFECTIVE", $"starts on {contract.EffectiveFrom:dd MMM yyyy}"),
            _ when input.Date > contract.EffectiveTo => ("CONTRACT_EXPIRED", $"ended on {contract.EffectiveTo:dd MMM yyyy}"),
            _ => ("CONTRACT_NOT_ACTIVE", $"is {contract.Status}, not active"),
        };
    }

    /// <summary>How specifically the rate's lane matches the shipment (6 exact city lane, 4 zone, 2 state, 0 anywhere), or null when it is a different lane.</summary>
    private static int? LaneScore(RateCard card, RatingInput input, Func<string, Zone?> zones)
    {
        var origin = card.Origin;
        var destination = card.Destination;
        var forward = origin.Matches(input.Origin, zones) && destination.Matches(input.Destination, zones);
        var reverse = card.BothWays && origin.Matches(input.Destination, zones) && destination.Matches(input.Origin, zones);
        return forward || reverse ? origin.Specificity + destination.Specificity : null;
    }

    private static int Bands(RateCard card) => (card.HasDistanceBand ? 1 : 0) + (card.HasWeightBand ? 1 : 0) + (card.HasVolumeBand ? 1 : 0);

    private static (string Code, string Reason)? RateExclusion(RateCard card, RatingInput input)
    {
        if (card.Pricing.ServiceType() != input.Service)
        {
            return ("SERVICE_MISMATCH", $"is a {card.Pricing.ServiceType()} rate, but {input.Service} was asked for");
        }

        if (card.ValidTo is { } to && input.Date > to)
        {
            return ("RATE_EXPIRED", $"expired on {to:dd MMM yyyy}");
        }

        if (card.ValidFrom is { } from && input.Date < from)
        {
            return ("RATE_NOT_YET_VALID", $"starts on {from:dd MMM yyyy}");
        }

        if (card.VehicleTypeId is { } vehicle && input.VehicleTypeId != vehicle)
        {
            return ("VEHICLE_MISMATCH", input.VehicleTypeId is null ? "is for a specific vehicle type and none was given" : "vehicle type mismatch");
        }

        if (Band(card.MinWeightKg, card.MaxWeightKg, input.WeightKg, "weight", "kg", "WEIGHT") is { } w)
        {
            return w;
        }

        if (Band(card.MinDistanceKm, card.MaxDistanceKm, input.DistanceKm, "distance", "km", "DISTANCE") is { } d)
        {
            return d;
        }

        if (Band(card.MinVolumeCbm, card.MaxVolumeCbm, input.VolumeCbm, "volume", "CBM", "VOLUME") is { } v)
        {
            return v;
        }

        var have = input.Capabilities.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
        return card.RequiredCapabilities.FirstOrDefault(c => !have.Contains(c)) is { } missing
            ? ("CAPABILITY_MISMATCH", $"is only for shipments that need {missing}")
            : null;
    }

    private static (string Code, string Reason)? Band(decimal? min, decimal? max, decimal? value, string name, string unit, string code)
    {
        if (min is null && max is null)
        {
            return null;
        }

        if (value is not > 0)
        {
            return ($"{code}_REQUIRED", $"The {name} in {unit} is needed to choose a {name} slab");
        }

        return value < (min ?? 0m) || (max is { } hi && value > hi)
            ? ($"{code}_SLAB_MISMATCH", $"{name} slab {min ?? 0:0.##}–{(max is { } m ? $"{m:0.##}" : "no limit")} {unit} does not apply to {value:0.##} {unit}")
            : null;
    }

    private static string WhyLoser(Candidate loser, Candidate winner) =>
        loser.Geo != winner.Geo ? $"{GeoName(loser.Geo)} rate is lower priority than the {GeoName(winner.Geo)} rate"
        : loser.Vehicle != winner.Vehicle ? "A general rate is lower priority than the vehicle-specific rate"
        : loser.Capabilities != winner.Capabilities ? "A rate for fewer special services is lower priority than the more specific one"
        : loser.Bands != winner.Bands ? "A rate with fewer slab conditions is lower priority than the more specific one"
        : $"Preference {loser.Priority} is lower than preference {winner.Priority}";

    private static string GeoName(int geo) => geo switch
    {
        6 => "exact lane",
        4 => "zone",
        2 => "state (region)",
        0 => "default",
        _ => "partial lane",
    };

    // ---- calculation

    private static Result<RatingBreakdown> Calculate(Contract contract, RateCard card, RatingInput input, RatingContext context, List<TraceStep> trace)
    {
        var terms = contract.Terms;
        var query = new FreightQuery(input.Date, input.Origin, input.Destination, input.VehicleTypeId, input.Service, input.WeightKg, input.VolumeCbm, input.DistanceKm, Math.Max(1, input.StopCount), input.Packages);
        var lines = new List<RatingLine>();
        var notes = new List<string>();
        decimal? chargeable = null;
        decimal baseAmount;

        var reference = $"{card.Code} V{card.Version}";
        if (card.Pricing is DedicatedPricing dedicated)
        {
            if (!input.AccessorialInputs.TryGetValue("KM_RUN", out var km) | !input.AccessorialInputs.TryGetValue("HOURS_RUN", out var hours))
            {
                notes.Add("Dedicated vehicles are billed monthly: only the rental is rated. Give KM_RUN and HOURS_RUN to include kilometres and hours beyond the allowance.");
            }

            var month = FreightCalculator.CalculateDedicatedMonth(contract, card, km, hours, null);
            if (month.IsFailure)
            {
                return month.Error;
            }

            baseAmount = dedicated.MonthlyRental;
            lines.Add(new RatingLine("BASE_FREIGHT", $"Monthly rental ₹{dedicated.MonthlyRental:0.##}", 1, "MONTH", dedicated.MonthlyRental, FreightCalculator.Round(baseAmount), reference));
            foreach (var l in month.Value.Lines.Where(l => l.Code != "RENTAL" && l.Code != "FUEL"))
            {
                lines.Add(new RatingLine(l.Code, l.Description, null, null, null, l.Amount, reference));
            }
        }
        else
        {
            var computed = FreightCalculator.BaseFreight(terms, card.Pricing, query);
            if (computed.IsFailure)
            {
                return computed.Error;
            }

            var (amount, basis, weight, baseNotes) = computed.Value;
            (amount, basis) = FreightCalculator.ApplyCardLimits(card, amount, basis);
            chargeable = weight;
            notes.AddRange(baseNotes);
            baseAmount = amount;
            lines.Add(new RatingLine("BASE_FREIGHT", basis, null, null, null, FreightCalculator.Round(amount), reference));
        }

        trace.Add(new TraceStep("Base freight", $"{reference}: {lines[0].Description} = ₹{lines[0].Amount:0.##}"));
        var baseFreight = lines[0].Amount;

        // DPH: the rule version in force on the shipment date, priced from the index for the rule's period.
        DphRule? rule = null;
        DphResult? dph = null;
        if (contract.DphRules.Count > 0)
        {
            rule = DphCalculator.InForce(contract.DphRules, card.DphRuleCode, input.Date, contract.EffectiveFrom, contract.EffectiveTo);
            if (rule is null)
            {
                notes.Add(card.DphRuleCode is { } code
                    ? $"DPH rule {code} has no version in force on {input.Date:dd MMM yyyy}; no fuel adjustment applied"
                    : "No default DPH rule is in force on that date; no fuel adjustment applied");
            }
            else
            {
                var period = DphCalculator.ReferenceDate(rule.Spec.Frequency, input.Date);
                var price = context.Snapshots is not null && context.Snapshots.TryGetValue((rule.Id, period), out var snapshot)
                    ? snapshot.ReferencePrice
                    : DieselPrice.Resolve(context.Diesel, rule.Spec.Region, period);
                if (price is null)
                {
                    notes.Add($"No diesel price on file for {rule.Spec.Region} on {period:dd MMM yyyy}; no fuel adjustment applied");
                }
                else
                {
                    dph = DphCalculator.Calculate(rule.Spec, price.Value, period, baseFreight, input.DistanceKm);
                    if (dph.Applied)
                    {
                        lines.Add(new RatingLine("DPH", dph.Explanation, null, null, dph.AdjustmentPercent, dph.Amount, $"{rule.Code} V{rule.Version}"));
                    }
                    else
                    {
                        notes.Add(dph.Explanation);
                    }

                    trace.Add(new TraceStep("DPH", $"{rule.Code} V{rule.Version}: {dph.Explanation}"));
                }
            }
        }
        else if (contract.Fuel is { } clause)
        {
            var price = DieselPrice.Resolve(context.Diesel, clause.Region, input.Date);
            if (price is null)
            {
                notes.Add($"No diesel price on file for {clause.Region}; no fuel adjustment applied");
            }
            else
            {
                var adjustment = clause.Evaluate(price.Value);
                if (adjustment.Percent == 0)
                {
                    notes.Add(adjustment.Explanation);
                }
                else
                {
                    lines.Add(new RatingLine("DPH", adjustment.Explanation, null, null, adjustment.Percent, FreightCalculator.Round(baseFreight * adjustment.Percent / 100m), "diesel clause"));
                }

                trace.Add(new TraceStep("DPH", adjustment.Explanation));
            }
        }

        var dphAmount = lines.Where(l => l.Type == "DPH").Sum(l => l.Amount);

        decimal discount = 0;
        if (terms.DiscountPercent is > 0 and var pct)
        {
            discount = -FreightCalculator.Round((baseFreight + dphAmount) * pct / 100m);
            lines.Add(new RatingLine("DISCOUNT", $"Contract discount {pct:0.##}% of base freight and DPH", null, null, pct, discount, null));
            trace.Add(new TraceStep("Discount", $"{pct:0.##}% of ₹{baseFreight + dphAmount:0.##} = ₹{-discount:0.##}"));
        }

        var accessorials = 0m;
        var capabilities = input.Capabilities.Select(c => c.Trim().ToUpperInvariant()).ToHashSet();
        var context2 = new AccessorialContext(input.Date, input.Service, input.WeightKg ?? 0, Math.Max(1, input.StopCount), capabilities, input.AccessorialInputs, baseFreight);
        foreach (var spec in AccessorialSpecs(contract))
        {
            if (AccessorialCalculator.Calculate(spec, context2) is { } line)
            {
                lines.Add(new RatingLine(line.Code, line.Description, line.Quantity, line.Unit, line.Rate, line.Amount, "contract accessorial"));
                accessorials += line.Amount;
                trace.Add(new TraceStep("Accessorials", line.Description + $" = ₹{line.Amount:0.##}"));
            }
        }

        // Dedicated extras (km, hours) were added as lines above; they are accessorial in nature.
        accessorials += lines.Where(l => l.Type is "EXTRA_KM" or "EXTRA_HOURS" && l.Reference == reference).Sum(l => l.Amount);

        var subtotal = lines.Sum(l => l.Amount);
        var rounding = terms.Rounding ?? RoundingRule.Paise;
        var rounded = rounding.Apply(subtotal);
        if (rounded != subtotal)
        {
            lines.Add(new RatingLine("ROUNDING", $"Rounded to the nearest ₹{rounding.Increment:0.##}{(rounding.Mode == RoundingMode.Nearest ? string.Empty : $" ({rounding.Mode.ToString().ToLowerInvariant()})")}", null, null, null, rounded - subtotal, null));
            trace.Add(new TraceStep("Rounding", $"₹{subtotal:0.##} → ₹{rounded:0.##}"));
        }

        var total = lines.Sum(l => l.Amount);
        var reasons = Reasons(card, input, contract);
        return new RatingBreakdown(contract, card, lines, baseFreight, dphAmount, accessorials, discount, total, rule, dph, contract.Currency, notes, chargeable, reasons);
    }

    /// <summary>The contract's own charges, plus the older terms-based ones for any code the contract does not define itself (so nothing is charged twice).</summary>
    internal static IReadOnlyList<AccessorialSpec> AccessorialSpecs(Contract contract)
    {
        var specs = contract.Accessorials.Select(a => a.Spec).ToList();
        var defined = specs.Select(s => s.Code.ToUpperInvariant()).ToHashSet();
        var terms = contract.Terms;
        if (terms.LoadingCharge > 0 && defined.Add("LOADING"))
        {
            specs.Add(new AccessorialSpec("LOADING", "Loading charge", AccessorialCalc.Fixed, "TRIP", terms.LoadingCharge, AutoApply: true));
        }

        if (terms.UnloadingCharge > 0 && defined.Add("UNLOADING"))
        {
            specs.Add(new AccessorialSpec("UNLOADING", "Unloading charge", AccessorialCalc.Fixed, "TRIP", terms.UnloadingCharge, AutoApply: true));
        }

        if (terms.MultiDropChargePerPoint > 0 && defined.Add("MULTI_DROP") && !defined.Contains("ADDITIONAL_STOP"))
        {
            specs.Add(new AccessorialSpec("MULTI_DROP", "Extra drop points", AccessorialCalc.PerUnit, "STOP", terms.MultiDropChargePerPoint, IncludedQuantity: 1));
        }

        if (terms.DetentionRatePerHour > 0 && defined.Add("DETENTION"))
        {
            specs.Add(new AccessorialSpec("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR", Tiers:
                [new AccessorialTier(0, terms.DetentionFreeHours, 0), new AccessorialTier(terms.DetentionFreeHours, null, terms.DetentionRatePerHour)]));
        }

        return specs;
    }

    private static List<string> Reasons(RateCard card, RatingInput input, Contract contract)
    {
        var reasons = new List<string> { $"✓ Active contract {contract.Reference} on {input.Date:dd MMM yyyy}" };
        reasons.Add($"✓ {GeoName(card.Origin.Specificity + card.Destination.Specificity)}: {card.Origin} → {card.Destination}");
        reasons.Add($"✓ {card.Pricing.ServiceType()}");
        if (card.VehicleTypeId is not null)
        {
            reasons.Add("✓ Vehicle type");
        }

        if (card.HasWeightBand)
        {
            reasons.Add($"✓ Weight slab {card.MinWeightKg ?? 0:0.##}–{(card.MaxWeightKg is { } w ? $"{w:0.##}" : "no limit")} kg");
        }

        if (card.HasDistanceBand)
        {
            reasons.Add($"✓ Distance slab {card.MinDistanceKm ?? 0:0.##}–{(card.MaxDistanceKm is { } d ? $"{d:0.##}" : "no limit")} km");
        }

        if (card.HasVolumeBand)
        {
            reasons.Add($"✓ Volume slab {card.MinVolumeCbm ?? 0:0.##}–{(card.MaxVolumeCbm is { } v ? $"{v:0.##}" : "no limit")} CBM");
        }

        if (card.RequiredCapabilities.Count > 0)
        {
            reasons.Add($"✓ Special services: {string.Join(", ", card.RequiredCapabilities)}");
        }

        if (card.Priority != RateExtras.DefaultPriority)
        {
            reasons.Add($"✓ Preference {card.Priority}");
        }

        return reasons;
    }

    private static string Lane(Location from, Location to) => $"{from.City ?? from.State} → {to.City ?? to.State}";

    private static string Num(decimal? value, string unit) => value is > 0 ? $"{value:0.##} {unit}" : $"no {unit}";
}
