using System.Globalization;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Contracts.Application.Import;

/// <summary>What a rate sheet's columns mean, and how a row becomes a rate. Pure apart from the lookups it is given.</summary>
internal static class RateSheet
{
    /// <summary>The columns of the template, in order. Headers are matched without regard to case or spacing.</summary>
    public static readonly string[] Columns =
    [
        "Contract Number", "Contract Version", "Transporter", "Service Type", "Origin", "Destination", "Origin Zone", "Destination Zone", "Vehicle Type",
        "Weight From", "Weight To", "Distance From", "Distance To", "Volume From", "Volume To", "Rate", "Rate Type", "Minimum Charge", "Maximum Charge", "DPH Rule", "Priority",
        "Effective From", "Effective To", "Rate Code", "Both Ways", "Capabilities", "Included KM", "Extra KM Rate", "Included Hours", "Extra Hour Rate",
    ];

    public static readonly string[] RateTypes = ["FIXED", "PER_KM", "PER_KG", "PER_TON", "PER_CBM", "PER_BOX", "MONTHLY"];

    public sealed record Lookups(IReadOnlyList<VehicleTypeInfo> VehicleTypes, IReadOnlySet<string> ZoneCodes);

    public sealed record Parsed(RateCardSpec? Spec, string? ContractNumber, int? ContractRevision, string? Transporter, List<RateIssue> Issues);

    private static string Key(string column) => Sheets.Normalise(column);

    public static string? Get(IReadOnlyDictionary<string, string?> row, string column) => row.TryGetValue(Key(column), out var v) ? v : null;

    public static Parsed Parse(IReadOnlyDictionary<string, string?> row, int rowNumber, Lookups lookups)
    {
        var issues = new List<RateIssue>();
        void Error(string field, string code, string message) => issues.Add(new RateIssue(IssueSeverity.Error, rowNumber, field, code, message));

        decimal? Number(string column, bool required = false)
        {
            var text = Get(row, column);
            if (text is null)
            {
                if (required)
                {
                    Error(column, "ROW_REQUIRED", $"{column} is required.");
                }

                return null;
            }

            if (decimal.TryParse(text.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                return value;
            }

            Error(column, "ROW_NUMBER_INVALID", $"{column} '{text}' is not a valid number.");
            return null;
        }

        DateOnly? Date(string column)
        {
            var text = Get(row, column);
            if (text is null)
            {
                return null;
            }

            string[] formats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "d/M/yyyy", "d-M-yyyy"];
            if (DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            Error(column, "ROW_DATE_INVALID", $"{column} '{text}' is not a date. Use yyyy-MM-dd.");
            return null;
        }

        var service = ParseService(Get(row, "Service Type"));
        if (service is null)
        {
            Error("Service Type", "ROW_SERVICE_INVALID", $"Service Type '{Get(row, "Service Type")}' must be FTL, PTL or DEDICATED.");
        }

        var origin = ParsePlace(Get(row, "Origin"), Get(row, "Origin Zone"), "Origin", lookups, Error);
        var destination = ParsePlace(Get(row, "Destination"), Get(row, "Destination Zone"), "Destination", lookups, Error);

        Guid? vehicle = null;
        if (Get(row, "Vehicle Type") is { } vehicleText)
        {
            var match = lookups.VehicleTypes.FirstOrDefault(v => string.Equals(v.Name, vehicleText, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Code, vehicleText, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                Error("Vehicle Type", "ROW_VEHICLE_UNKNOWN", $"Vehicle type '{vehicleText}' is not one of the active vehicle types.");
            }
            else
            {
                vehicle = match.Id;
            }
        }

        var weightFrom = Number("Weight From");
        var weightTo = Number("Weight To");
        var distanceFrom = Number("Distance From");
        var distanceTo = Number("Distance To");
        var volumeFrom = Number("Volume From");
        var volumeTo = Number("Volume To");
        var rate = Number("Rate", required: true);
        var minimum = Number("Minimum Charge");
        var maximum = Number("Maximum Charge");
        var from = Date("Effective From");
        var to = Date("Effective To");

        var priority = RateExtras.DefaultPriority;
        if (Get(row, "Priority") is { } priorityText && !int.TryParse(priorityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
        {
            Error("Priority", "ROW_NUMBER_INVALID", $"Priority '{priorityText}' is not a whole number.");
        }

        var bothWays = Get(row, "Both Ways") is { } bw && bw.ToUpperInvariant() is "YES" or "Y" or "TRUE" or "1";
        var capabilities = Get(row, "Capabilities")?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var rateType = (Get(row, "Rate Type") ?? (service == ContractType.Ptl ? "PER_KG" : service == ContractType.Dedicated ? "MONTHLY" : "FIXED")).Trim().ToUpperInvariant().Replace(' ', '_');
        if (!RateTypes.Contains(rateType))
        {
            Error("Rate Type", "ROW_RATE_TYPE_INVALID", $"Rate Type '{rateType}' must be one of {string.Join(", ", RateTypes)}.");
        }

        Pricing? pricing = null;
        if (service is { } svc && rate is { } amount && RateTypes.Contains(rateType))
        {
            pricing = BuildPricing(svc, rateType, amount, minimum, Number("Included KM"), Number("Extra KM Rate"), Number("Included Hours"), Number("Extra Hour Rate"), Error);
        }

        var spec = pricing is not null && origin is not null && destination is not null && issues.Count == 0
            ? new RateCardSpec(origin, destination, bothWays, vehicle, distanceFrom, distanceTo, pricing,
                new RateExtras(Get(row, "Rate Code"), priority, minimum, maximum, weightFrom, weightTo, volumeFrom, volumeTo, from, to, capabilities, Get(row, "DPH Rule")))
            : null;
        int? revision = null;
        if (Get(row, "Contract Version") is { } versionText)
        {
            if (int.TryParse(versionText.TrimStart('V', 'v'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r > 0)
            {
                revision = r;
            }
            else
            {
                Error("Contract Version", "ROW_NUMBER_INVALID", $"Contract Version '{versionText}' is not a version number.");
            }
        }

        return new Parsed(spec, Get(row, "Contract Number")?.Trim().ToUpperInvariant(), revision, Get(row, "Transporter"), issues);
    }

    private static ContractType? ParseService(string? text) => text?.Trim().ToUpperInvariant() switch
    {
        "FTL" => ContractType.Ftl,
        "PTL" => ContractType.Ptl,
        "DEDICATED" => ContractType.Dedicated,
        _ => null,
    };

    private static Pricing? BuildPricing(
        ContractType service, string rateType, decimal rate, decimal? minimum, decimal? includedKm, decimal? extraKm, decimal? includedHours, decimal? extraHour, Action<string, string, string> error)
    {
        if (rate <= 0)
        {
            error("Rate", "ROW_RATE_INVALID", "The rate must be more than zero.");
            return null;
        }

        switch (rateType)
        {
            case "FIXED" when service == ContractType.Ftl:
                return new FlatTripPricing(rate);
            case "FIXED" when service == ContractType.Ptl:
                return new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Trip, SlabMethod.Flat, [new Slab(0, null, rate, SlabRateType.Fixed)]);
            case "PER_KM" when service == ContractType.Ftl:
                return new PerKmPricing(rate, 0, minimum ?? 0);
            case "PER_KG" or "PER_TON":
                return new SlabRatePricing(service, SlabDimension.Weight, rateType == "PER_KG" ? RateUnit.Kg : RateUnit.Ton, SlabMethod.Flat, [new Slab(0, null, rate)]);
            case "PER_CBM":
                return new SlabRatePricing(service, SlabDimension.Volume, RateUnit.Cbm, SlabMethod.Flat, [new Slab(0, null, rate)]);
            case "PER_BOX":
                return new SlabRatePricing(service, SlabDimension.Packages, RateUnit.Box, SlabMethod.Flat, [new Slab(0, null, rate)]);
            case "MONTHLY" when service == ContractType.Dedicated:
                return new DedicatedPricing(rate, includedKm ?? 0, extraKm ?? 0, includedHours ?? 0, extraHour ?? 0);
            default:
                error("Rate Type", "ROW_RATE_TYPE_MISMATCH", $"{rateType} cannot price a {service} rate.");
                return null;
        }
    }

    private static Place? ParsePlace(string? text, string? zone, string column, Lookups lookups, Action<string, string, string> error)
    {
        if (zone is not null)
        {
            var code = zone.Trim().ToUpperInvariant();
            if (!lookups.ZoneCodes.Contains(code))
            {
                error($"{column} Zone", "ROW_ZONE_UNKNOWN", $"Zone '{zone}' is not defined. Add it under Zones first.");
                return null;
            }

            return Place.OfZone(code);
        }

        if (text is null || text.Trim().ToUpperInvariant() is "ANY" or "*" or "ANYWHERE")
        {
            if (text is null)
            {
                error(column, "ROW_LANE_MISSING", $"{column} (or {column} Zone) is required. Write Any for anywhere.");
                return null;
            }

            return Place.Anywhere;
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => Place.OfState(parts[0]),
            2 => Place.OfCity(parts[1], parts[0]),
            _ => Fail(),
        };

        Place? Fail()
        {
            error(column, "ROW_LANE_INVALID", $"{column} '{text}' should be 'City, State', a state, or Any.");
            return null;
        }
    }

    /// <summary>A rate as a sheet row (for export). Multi-slab tables are written one row per slab where that is exact, and flagged where it is not.</summary>
    public static IEnumerable<Dictionary<string, object?>> ToRows(Contract contract, RateCard card, string transporter, string? vehicleName)
    {
        Dictionary<string, object?> Base() => new()
        {
            ["Contract Number"] = contract.Number, ["Contract Version"] = contract.Revision, ["Transporter"] = transporter, ["Service Type"] = card.Pricing.ServiceType().ToString().ToUpperInvariant(),
            ["Origin"] = PlaceText(card.Origin), ["Destination"] = PlaceText(card.Destination), ["Origin Zone"] = card.Origin.ZoneCode, ["Destination Zone"] = card.Destination.ZoneCode,
            ["Vehicle Type"] = vehicleName, ["Weight From"] = card.MinWeightKg, ["Weight To"] = card.MaxWeightKg, ["Distance From"] = card.MinDistanceKm, ["Distance To"] = card.MaxDistanceKm,
            ["Volume From"] = card.MinVolumeCbm, ["Volume To"] = card.MaxVolumeCbm, ["Rate"] = null, ["Rate Type"] = null, ["Minimum Charge"] = card.MinimumCharge, ["Maximum Charge"] = card.MaximumCharge,
            ["DPH Rule"] = card.DphRuleCode, ["Priority"] = card.Priority, ["Effective From"] = card.ValidFrom ?? contract.EffectiveFrom, ["Effective To"] = card.ValidTo ?? contract.EffectiveTo,
            ["Rate Code"] = card.Code, ["Both Ways"] = card.BothWays ? "YES" : null, ["Capabilities"] = string.Join(", ", card.RequiredCapabilities), ["Included KM"] = null, ["Extra KM Rate"] = null,
            ["Included Hours"] = null, ["Extra Hour Rate"] = null,
        };

        switch (card.Pricing)
        {
            case FlatTripPricing f:
            {
                var r = Base(); r["Rate"] = f.AmountPerTrip; r["Rate Type"] = "FIXED"; yield return r; break;
            }

            case PerKmPricing k:
            {
                var r = Base(); r["Rate"] = k.RatePerKm; r["Rate Type"] = "PER_KM"; r["Minimum Charge"] ??= k.MinCharge; yield return r; break;
            }

            case DedicatedPricing d:
            {
                var r = Base(); r["Rate"] = d.MonthlyRental; r["Rate Type"] = "MONTHLY"; r["Included KM"] = d.IncludedKmPerMonth; r["Extra KM Rate"] = d.ExtraKmRate; r["Included Hours"] = d.IncludedHoursPerMonth; r["Extra Hour Rate"] = d.ExtraHourRate; yield return r; break;
            }

            case WeightSlabPricing w when w.Mode == SlabMode.Whole:
                foreach (var slab in w.Slabs)
                {
                    var r = Base(); r["Rate"] = slab.RatePerKg; r["Rate Type"] = "PER_KG"; r["Weight From"] = slab.FromKg; r["Weight To"] = slab.ToKg; yield return r;
                }

                break;

            case SlabRatePricing s when s.Method == SlabMethod.Flat:
                foreach (var slab in s.Slabs)
                {
                    var r = Base();
                    r["Rate"] = slab.Rate;
                    r["Rate Type"] = slab.Type == SlabRateType.Fixed ? "FIXED" : s.Unit switch { RateUnit.Ton => "PER_TON", RateUnit.Cbm => "PER_CBM", RateUnit.Box => "PER_BOX", _ => "PER_KG" };
                    switch (s.Dimension)
                    {
                        case SlabDimension.Weight: r["Weight From"] = slab.From; r["Weight To"] = slab.To; break;
                        case SlabDimension.Distance: r["Distance From"] = slab.From; r["Distance To"] = slab.To; break;
                        case SlabDimension.Volume: r["Volume From"] = slab.From; r["Volume To"] = slab.To; break;
                    }

                    yield return r;
                }

                break;

            default:
            {
                var r = Base(); r["Rate Type"] = RateSummary(card.Pricing); r["Rate"] = null; yield return r; break;
            }
        }
    }

    private static string RateSummary(Pricing p) => p is SlabRatePricing { Method: SlabMethod.Progressive } ? "PROGRESSIVE (edit on screen)" : p is SlabRatePricing { Method: SlabMethod.BaseExcess } ? "BASE_EXCESS (edit on screen)" : "OTHER";

    private static string? PlaceText(Place p) => p.Kind switch
    {
        PlaceKind.City => $"{Title(p.City!)}, {Title(p.State!)}",
        PlaceKind.State => Title(p.State!),
        PlaceKind.Any => "Any",
        _ => null,
    };

    private static string Title(string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
}
