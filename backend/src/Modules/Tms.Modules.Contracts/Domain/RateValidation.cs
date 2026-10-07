namespace Tms.Modules.Contracts.Domain;

public enum IssueSeverity
{
    Error = 1,
    Warning = 2,
}

/// <param name="Row">The 1-based row (a rate's position in the list, or its line in an import), when the problem belongs to one.</param>
public sealed record RateIssue(IssueSeverity Severity, int? Row, string Field, string Code, string Message);

public sealed record RateValidationResult(IReadOnlyList<RateIssue> Issues)
{
    public bool IsValid => Issues.All(i => i.Severity != IssueSeverity.Error);

    public int Errors => Issues.Count(i => i.Severity == IssueSeverity.Error);

    public int Warnings => Issues.Count(i => i.Severity == IssueSeverity.Warning);
}

/// <summary>A rate to be checked, with the period it is valid for and where it came from (named in messages about clashes with other contracts).</summary>
public sealed record RateToCheck(int Row, RateCardSpec Spec, DateOnly From, DateOnly To, string Owner);

/// <summary>
/// Checks a set of rates before they can be approved or activated: each one on its own, then against each other and against rates already in force. Pure. An error blocks
/// activation; a warning is shown but does not.
/// </summary>
public static class RateValidator
{
    public static RateValidationResult Validate(
        IReadOnlyList<RateToCheck> rates,
        DateOnly contractFrom,
        DateOnly contractTo,
        IReadOnlyCollection<ContractType> services,
        IReadOnlySet<string> dphCodes,
        IReadOnlyList<RateToCheck>? inForce = null)
    {
        var issues = new List<RateIssue>();
        void Error(RateToCheck r, string field, string code, string message) => issues.Add(new RateIssue(IssueSeverity.Error, r.Row, field, code, message));
        void Warn(RateToCheck r, string field, string code, string message) => issues.Add(new RateIssue(IssueSeverity.Warning, r.Row, field, code, message));

        foreach (var r in rates)
        {
            var spec = r.Spec;
            var x = spec.Extras ?? RateExtras.None;
            var service = spec.Pricing.ServiceType();

            if (spec.Pricing.Validate() is { IsFailure: true } priced)
            {
                Error(r, priced.Error.ValidationErrors?.Keys.FirstOrDefault() ?? "pricing", "RATE_PRICING_INVALID", priced.Error.Description);
            }

            if (!services.Contains(service))
            {
                Error(r, "service", "RATE_SERVICE_NOT_COVERED", $"This rate is for {service}, which the contract does not cover.");
            }

            if (service is ContractType.Ftl or ContractType.Dedicated && spec.VehicleTypeId is null)
            {
                Error(r, "vehicleType", "RATE_VEHICLE_MISSING", "Choose a vehicle type.");
            }

            if (service == ContractType.Ptl && spec.VehicleTypeId is not null)
            {
                Error(r, "vehicleType", "RATE_VEHICLE_NOT_ALLOWED", "Part-load rates are not tied to a vehicle type.");
            }

            if (spec.MinDistanceKm is < 0 || spec.MaxDistanceKm is < 0 || (spec.MinDistanceKm is { } lo && spec.MaxDistanceKm is { } hi && hi <= lo))
            {
                Error(r, "distance", "RATE_RANGE_INVALID", $"Distance From {spec.MinDistanceKm} and To {spec.MaxDistanceKm} is not a valid range.");
            }

            if (x.Validate() is { IsFailure: true } extras)
            {
                Error(r, extras.Error.ValidationErrors?.Keys.FirstOrDefault() ?? "rate", "RATE_TERMS_INVALID", extras.Error.Description);
            }

            if (spec.Origin.Kind == PlaceKind.Any && spec.Destination.Kind == PlaceKind.Any && spec.MinDistanceKm is null && spec.MaxDistanceKm is null)
            {
                Error(r, "lane", "RATE_LANE_MISSING", "A rate for anywhere-to-anywhere needs a distance band, otherwise it applies to every shipment.");
            }

            if (x.DphRuleCode is { Length: > 0 } code && !dphCodes.Contains(code.Trim().ToUpperInvariant()))
            {
                Error(r, "dphRule", "RATE_DPH_UNKNOWN", $"DPH rule {code.Trim().ToUpperInvariant()} is not defined on the contract.");
            }

            if (r.From > r.To)
            {
                Error(r, "validity", "RATE_DATES_INVALID", "The rate ends before it starts.");
            }
            else if (r.From < contractFrom || r.To > contractTo)
            {
                Warn(r, "validity", "RATE_OUTSIDE_CONTRACT", $"The rate's dates ({r.From:dd MMM yyyy}–{r.To:dd MMM yyyy}) go beyond the contract's ({contractFrom:dd MMM yyyy}–{contractTo:dd MMM yyyy}); only the overlap counts.");
            }
        }

        // Rates that could price the same shipment are compared in groups, so thousands of rates do not mean millions of comparisons.
        var group = (RateToCheck r) => (r.Spec.Pricing.ServiceType(), r.Spec.VehicleTypeId, string.Join(",", (r.Spec.Extras?.RequiredCapabilities ?? []).Select(c => c.Trim().ToUpperInvariant()).Order()), LaneKey(r.Spec));
        var existing = inForce ?? [];
        var fresh = new HashSet<RateToCheck>(rates, ReferenceEqualityComparer.Instance);
        foreach (var bucket in rates.Concat(existing).GroupBy(group))
        {
            var items = bucket.ToList();
            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    var (a, b) = (items[i], items[j]);
                    var aNew = fresh.Contains(a);
                    var bNew = fresh.Contains(b);
                    if (!aNew && !bNew)
                    {
                        continue; // two rates already in force were checked when they were approved
                    }

                    if (!SameLane(a.Spec, b.Spec) || a.From > b.To || b.From > a.To || !BandsIntersect(a.Spec, b.Spec))
                    {
                        continue;
                    }

                    var pa = a.Spec.Extras?.Priority ?? RateExtras.DefaultPriority;
                    var pb = b.Spec.Extras?.Priority ?? RateExtras.DefaultPriority;
                    var lane = $"{a.Spec.Origin} → {a.Spec.Destination}";
                    // Two new rows: the later one is reported, pointing back at the earlier. A new row against one already in force: the new one is reported.
                    var subject = aNew && bNew ? (a.Row >= b.Row ? a : b) : aNew ? a : b;
                    var other = ReferenceEquals(subject, a) ? b : a;
                    var where = fresh.Contains(other) ? $"row {other.Row}" : $"a rate already in force on {other.Owner}";
                    if (Key(a.Spec) == Key(b.Spec) && a.From == b.From && a.To == b.To)
                    {
                        Error(subject, "rate", "RATE_DUPLICATE", $"Duplicates {where} ({lane}, same vehicle, slab and validity).");
                    }
                    else if (pa == pb)
                    {
                        Error(subject, "rate", "RATE_CONFLICT", $"Rate conflict: this rate and {where} both qualify for the same lane ({lane}), vehicle, slab and validity period, with the same priority. Change a slab, a date or a priority.");
                    }
                    else
                    {
                        Warn(subject, "priority", "RATE_OVERLAP_PRIORITY", $"Overlaps {where} ({lane}); the rate with priority {Math.Min(pa, pb)} will be used where both qualify.");
                    }
                }
            }
        }

        return new RateValidationResult(issues.OrderBy(i => i.Row ?? int.MaxValue).ThenBy(i => i.Severity).ToList());
    }

    /// <summary>The two ends in a fixed order, so a lane and its reverse land in the same bucket (a both-ways rate covers both).</summary>
    private static string LaneKey(RateCardSpec s)
    {
        var (a, b) = (s.Origin.ToString(), s.Destination.ToString());
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }

    private static string Key(RateCardSpec s)
    {
        var x = s.Extras ?? RateExtras.None;
        return $"{s.Origin}|{s.Destination}|{s.VehicleTypeId}|{s.MinDistanceKm}|{s.MaxDistanceKm}|{x.MinWeightKg}|{x.MaxWeightKg}|{x.MinVolumeCbm}|{x.MaxVolumeCbm}|{x.Priority}";
    }

    /// <summary>Whether the two rates name the same lane (a both-ways rate also covers the reverse direction).</summary>
    private static bool SameLane(RateCardSpec a, RateCardSpec b) =>
        (a.Origin == b.Origin && a.Destination == b.Destination)
        || (a.BothWays && a.Origin == b.Destination && a.Destination == b.Origin)
        || (b.BothWays && b.Origin == a.Destination && b.Destination == a.Origin);

    private static bool BandsIntersect(RateCardSpec a, RateCardSpec b)
    {
        var (xa, xb) = (a.Extras ?? RateExtras.None, b.Extras ?? RateExtras.None);
        return Intersect(a.MinDistanceKm, a.MaxDistanceKm, b.MinDistanceKm, b.MaxDistanceKm)
               && Intersect(xa.MinWeightKg, xa.MaxWeightKg, xb.MinWeightKg, xb.MaxWeightKg)
               && Intersect(xa.MinVolumeCbm, xa.MaxVolumeCbm, xb.MinVolumeCbm, xb.MaxVolumeCbm)
               && SlabsIntersect(a.Pricing, b.Pricing);
    }

    /// <summary>Bands count from "above the lower limit up to and including the upper", so 100–200 and 200–300 touch but do not overlap.</summary>
    private static bool Intersect(decimal? aFrom, decimal? aTo, decimal? bFrom, decimal? bTo) =>
        (aFrom ?? 0m) < (bTo ?? decimal.MaxValue) && (bFrom ?? 0m) < (aTo ?? decimal.MaxValue);

    /// <summary>Two per-unit slab tables in the same dimension overlap when their covered ranges do; other pricing shapes cover everything.</summary>
    private static bool SlabsIntersect(Pricing a, Pricing b) =>
        a is not SlabRatePricing x || b is not SlabRatePricing y || x.Dimension != y.Dimension
        || Intersect(0, x.Slabs[^1].To, 0, y.Slabs[^1].To);
}
