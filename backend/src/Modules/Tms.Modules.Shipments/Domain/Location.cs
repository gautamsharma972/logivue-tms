using Tms.SharedKernel.Domain;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

public enum LocationType
{
    Depot = 1,
    Plant = 2,
    Warehouse = 3,
    Customer = 4,
    Supplier = 5,
    Other = 6,
}

/// <summary>A place goods are picked up from or delivered to, with the coordinates routing needs. Maintained by planners.</summary>
public sealed class Location : AggregateRoot, ITenantScoped
{
    private Location()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Tenant-unique short code, upper case (e.g. <c>PUNE-DC</c>).</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public LocationType Type { get; private set; }

    public string Line1 { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public string State { get; private set; } = null!;

    public string Pincode { get; private set; } = null!;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public bool IsActive { get; private set; } = true;

    public GeoPoint Point => new(Latitude, Longitude);

    public static Result<Location> Create(
        Guid tenantId, string code, string name, LocationType type, string line1, string city, string state, string pincode,
        double latitude, double longitude)
    {
        var location = new Location { TenantId = tenantId };
        var set = location.Set(code, name, type, line1, city, state, pincode, latitude, longitude, true);
        return set.IsFailure ? set.Error : location;
    }

    public Result Update(
        string code, string name, LocationType type, string line1, string city, string state, string pincode,
        double latitude, double longitude, bool isActive) =>
        Set(code, name, type, line1, city, state, pincode, latitude, longitude, isActive);

    private Result Set(
        string code, string name, LocationType type, string line1, string city, string state, string pincode,
        double latitude, double longitude, bool isActive)
    {
        var errors = new Dictionary<string, string[]>();

        void Need(string key, string? value, string label, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[key] = [$"{label} is required."];
            }
            else if (value.Trim().Length > max)
            {
                errors[key] = [$"{label} must be at most {max} characters."];
            }
        }

        Need("code", code, "Code", 30);
        Need("name", name, "Name", 200);
        Need("line1", line1, "Address", 200);
        Need("city", city, "City", 100);
        Need("state", state, "State", 100);

        if (!string.IsNullOrWhiteSpace(code) && !System.Text.RegularExpressions.Regex.IsMatch(code.Trim(), "^[A-Za-z0-9][A-Za-z0-9._-]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            errors["code"] = ["Use letters, digits, '.', '_' or '-' only."];
        }

        if (!Enum.IsDefined(type))
        {
            errors["type"] = ["Choose a location type."];
        }

        if (!IndianIdentifiers.IsValidPincode(pincode))
        {
            errors["pincode"] = ["Enter a valid 6-digit pincode."];
        }

        var point = new GeoPoint(latitude, longitude);
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || !point.IsInIndia)
        {
            errors["latitude"] = [$"Coordinates must be inside India (latitude {GeoPoint.MinLatitude}–{GeoPoint.MaxLatitude}, longitude {GeoPoint.MinLongitude}–{GeoPoint.MaxLongitude}). Check they are not swapped."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
        Line1 = line1.Trim();
        City = city.Trim();
        State = state.Trim();
        Pincode = pincode.Trim();
        Latitude = Math.Round(latitude, 6);
        Longitude = Math.Round(longitude, 6);
        IsActive = isActive;
        return Result.Success();
    }
}
