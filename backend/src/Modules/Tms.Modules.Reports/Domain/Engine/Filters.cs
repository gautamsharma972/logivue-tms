using System.Globalization;
using System.Text.Json;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Reports.Domain;

/// <summary>The names of the common filters. Not every report offers every one; a report offers those that make sense for its rows.</summary>
public static class FilterNames
{
    public const string FromDate = "fromDate";
    public const string ToDate = "toDate";
    public const string BusinessUnit = "businessUnit";
    public const string Origin = "origin";
    public const string Destination = "destination";
    public const string Zone = "zone";
    public const string Lane = "lane";
    public const string Region = "region";
    public const string Transporter = "transporter";
    public const string Vehicle = "vehicle";
    public const string VehicleType = "vehicleType";
    public const string Driver = "driver";
    public const string Customer = "customer";
    public const string ServiceType = "serviceType";
    public const string Shipment = "shipment";
    public const string Load = "load";
    public const string Trip = "trip";
    public const string Contract = "contract";
    public const string Status = "status";
    public const string Exception = "exception";
    public const string Period = "period";
    public const string Compare = "compare";
    public const string Search = "search";

    /// <summary>Filter → the dimension it narrows. Several names can narrow the same dimension (a service type is the "service" dimension).</summary>
    public static readonly IReadOnlyDictionary<string, string> Dimension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [BusinessUnit] = "businessUnit",
        [Origin] = "origin",
        [Destination] = "destination",
        [Zone] = "zone",
        [Lane] = "lane",
        [Region] = "region",
        [Transporter] = "transporter",
        [Vehicle] = "vehicle",
        [VehicleType] = "vehicleType",
        [Driver] = "driver",
        [Customer] = "customer",
        [ServiceType] = "service",
        [Shipment] = "shipment",
        [Load] = "load",
        [Trip] = "trip",
        [Contract] = "contract",
        [Status] = "status",
        [Exception] = "exception",
    };
}

/// <summary>
/// The dimensions a fact carries, by name. A dimension that is present (even with no value) is one the fact has; a filter on a dimension the fact
/// does not have does not apply to it. One mapping per fact type (<see cref="FactDims"/>), so "lane" or "region" means the same everywhere.
/// </summary>
public sealed class Dims
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public DateOnly? Date { get; init; }

    public Guid? TransporterId { get; init; }

    public Dims Set(string dimension, string? value)
    {
        _values[dimension] = value;
        return this;
    }

    public bool Carries(string dimension) => _values.ContainsKey(dimension);

    public string? Get(string dimension) => _values.GetValueOrDefault(dimension);

    public IEnumerable<KeyValuePair<string, string?>> All => _values;
}

/// <summary>The filters of one report request. Values are text; several values for a filter are separated by commas.</summary>
public sealed class ReportFilters
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public ReportFilters()
    {
    }

    public ReportFilters(IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _values[key.Trim()] = value.Trim();
            }
        }
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public IReadOnlyList<string> Many(string name) =>
        Get(name) is { } v ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    public DateOnly? Date(string name) => DateOnly.TryParse(Get(name), CultureInfo.InvariantCulture, out var d) ? d : null;

    public ReportFilters With(string name, string? value)
    {
        var copy = new ReportFilters(_values.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
        if (string.IsNullOrWhiteSpace(value))
        {
            copy._values.Remove(name);
        }
        else
        {
            copy._values[name] = value.Trim();
        }

        return copy;
    }

    /// <summary>Reads a request's filter object. Scalars of any JSON type are accepted ("transporterId": 101 or "101"); arrays become comma lists; null and empty are dropped.</summary>
    public static ReportFilters FromJson(IReadOnlyDictionary<string, JsonElement>? filters)
    {
        var result = new ReportFilters();
        if (filters is null)
        {
            return result;
        }

        foreach (var (key, element) in filters)
        {
            var text = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
                JsonValueKind.Array => string.Join(',', element.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s))),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                result._values[key.Trim()] = text.Trim();
            }
        }

        return result;
    }

    /// <summary>True when the fact passes every filter that applies to it.</summary>
    public bool Matches(Dims dims, DateOnly from, DateOnly to)
    {
        if (dims.Date is { } d && (d < from || d > to))
        {
            return false;
        }

        foreach (var (name, dimension) in FilterNames.Dimension)
        {
            if (!_values.TryGetValue(name, out var wanted) || !dims.Carries(dimension))
            {
                continue;
            }

            var actual = dims.Get(dimension);
            if (actual is null)
            {
                return false;
            }

            var any = wanted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (dimension == "transporter" && dims.TransporterId is { } id)
            {
                if (!any.Any(w => w.Equals(actual, StringComparison.OrdinalIgnoreCase) || w.Equals(id.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                continue;
            }

            if (!any.Any(w => w.Equals(actual, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Text search over every dimension of a fact and, for rows, over their text columns.</summary>
    public bool MatchesSearch(IEnumerable<string?> texts) =>
        Get(FilterNames.Search) is not { } term || texts.Any(t => t is not null && t.Contains(term, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The one place each fact type says what its lane, region, customer and the other dimensions are. Every report uses it, so a lane is never defined twice.</summary>
public static class FactDims
{
    public static string LaneOf(string origin, string destination) => $"{origin} → {destination}";

    public static Dims Of(ShipmentReportFact f, ReportSettings s) => new Dims { Date = f.PlannedPickupDate, TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("origin", f.OriginCity).Set("destination", f.DestinationCity)
        .Set("region", f.Region).Set("customer", f.Customer).Set("vehicleType", f.VehicleType).Set("service", f.Service).Set("shipment", f.ShipmentRef)
        .Set("trip", f.ShipmentRef).Set("load", f.PlanRef).Set("contract", f.ContractRef).Set("vehicle", f.VehicleRef).Set("driver", f.DriverName)
        .Set("status", f.Status).Set("businessUnit", f.BusinessUnit);

    public static Dims Of(PlanningRunFact f) => new Dims { Date = f.PlanningDate }.Set("status", f.Status).Set("load", f.RunRef);

    public static Dims Of(PlanVehicleFact f) => new Dims { Date = f.PlanningDate, TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("region", f.Region).Set("vehicleType", f.VehicleType).Set("service", f.Service)
        .Set("trip", f.TripRef).Set("shipment", f.ShipmentRef).Set("vehicle", f.VehicleRef).Set("load", f.RunRef);

    public static Dims Of(UnplannedOrderFact f, ReportSettings s) => new Dims { Date = f.PlanningDate }
        .Set("lane", LaneOf(f.OriginCity, f.DestinationCity)).Set("origin", f.OriginCity).Set("destination", f.DestinationCity).Set("customer", f.Customer)
        .Set("region", s.RegionOf(f.OriginCity, null)).Set("load", f.RunRef).Set("status", f.ReasonCategory);

    public static Dims Of(TenderFact f, ReportSettings s) => new Dims { Date = DateOnly.FromDateTime(f.OfferedAt.UtcDateTime), TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("vehicleType", f.VehicleType).Set("service", f.Service).Set("shipment", f.ShipmentRef).Set("status", f.Outcome);

    public static Dims Of(TransporterFact f) => new Dims { TransporterId = f.Id }.Set("transporter", f.Name).Set("region", f.Region).Set("status", f.IsActive ? "Active" : "Inactive");

    public static Dims Of(ScorecardFact f) => new Dims { Date = f.PeriodEnd, TransporterId = f.TransporterId }.Set("transporter", f.TransporterName).Set("lane", f.Lane);

    public static Dims Of(PlacementFact f) => new Dims { Date = DateOnly.FromDateTime(f.RequiredBy.UtcDateTime), TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("vehicleType", f.VehicleType).Set("service", f.Service).Set("shipment", f.ShipmentRef).Set("status", f.Outcome);

    public static Dims Of(DeliveryFact f, ReportSettings s) => new Dims { Date = f.Date, TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("origin", f.OriginCity).Set("destination", f.DestinationCity).Set("customer", f.Customer)
        .Set("region", s.RegionOf(f.OriginCity, null)).Set("shipment", f.ShipmentRef).Set("trip", f.ShipmentRef).Set("status", f.Status);

    public static Dims Of(PodFact f) => new Dims { Date = f.Date, TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("customer", f.Customer).Set("shipment", f.ShipmentRef).Set("trip", f.ShipmentRef).Set("status", f.Status);

    public static Dims Of(DiscrepancyFact f) => new Dims { Date = f.Date, TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("customer", f.Customer).Set("shipment", f.ShipmentRef).Set("trip", f.ShipmentRef).Set("status", f.Type);

    public static Dims Of(TrackFact f, ReportSettings s) => new Dims { Date = f.Date, TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("vehicle", f.VehicleRef).Set("shipment", f.ShipmentRef).Set("trip", f.TripRef)
        .Set("region", s.RegionOf(f.Lane.Split('→')[0].Trim(), null)).Set("status", f.Risk);

    public static Dims Of(DeviationFact f) => new Dims { Date = DateOnly.FromDateTime(f.DetectedAt.UtcDateTime), TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("vehicle", f.VehicleRef).Set("shipment", f.ShipmentRef).Set("trip", f.TripRef).Set("status", f.Status);

    public static Dims Of(DwellFact f) => new Dims { Date = DateOnly.FromDateTime(f.At.UtcDateTime), TransporterId = f.TransporterId }
        .Set("shipment", f.ShipmentRef).Set("trip", f.TripRef).Set("status", f.Kind);

    public static Dims Of(GapFact f) => new Dims { Date = DateOnly.FromDateTime(f.GapStart.UtcDateTime), TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("vehicle", f.VehicleRef).Set("shipment", f.ShipmentRef).Set("trip", f.TripRef).Set("status", f.Status);

    public static Dims Of(ContractFact f) => new Dims { TransporterId = f.TransporterId }.Set("transporter", f.TransporterName).Set("contract", f.ContractRef).Set("service", f.Type).Set("status", f.Status);

    public static Dims Of(RateFact f) => new Dims { TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("contract", f.ContractRef).Set("origin", f.Origin).Set("destination", f.Destination).Set("lane", FactDims.LaneOf(f.Origin, f.Destination))
        .Set("zone", f.Zone).Set("vehicleType", f.VehicleType).Set("service", f.Service);

    public static Dims Of(DphFact f) => new Dims { TransporterId = f.TransporterId }.Set("transporter", f.TransporterName).Set("contract", f.ContractRef);

    public static Dims Of(RatingFact f, ReportSettings s) => new Dims { Date = DateOnly.FromDateTime(f.RatedAt.UtcDateTime), TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("contract", f.ContractRef).Set("lane", f.Lane).Set("shipment", f.ShipmentRef).Set("trip", f.ShipmentRef)
        .Set("service", f.Service).Set("vehicleType", f.VehicleType).Set("region", s.RegionOf(f.Lane.Split('→')[0].Trim(), null));

    public static Dims Of(CoverageFact f, ReportSettings s) => new Dims()
        .Set("lane", f.Lane).Set("origin", f.Origin).Set("destination", f.Destination).Set("service", f.Service).Set("region", s.RegionOf(f.Origin, null));

    public static Dims Of(ExceptionFact f) => new Dims { Date = DateOnly.FromDateTime(f.CreatedAt.UtcDateTime), TransporterId = f.TransporterId }
        .Set("transporter", f.TransporterName).Set("lane", f.Lane).Set("shipment", f.ShipmentRef).Set("trip", f.ShipmentRef).Set("exception", f.Type).Set("status", f.Status);
}
