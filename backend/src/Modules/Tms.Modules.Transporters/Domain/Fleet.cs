using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

/// <summary>A class of truck (e.g. "32 ft MXL, 16 T") that rate cards, planning and fleets all speak in.</summary>
public sealed class VehicleType : AggregateRoot, ITenantScoped
{
    private VehicleType()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public int PayloadKg { get; private set; }

    public decimal? VolumeCbm { get; private set; }

    /// <summary>Internal cargo-body dimensions in metres (optional). When set, planning checks the longest item against the length.</summary>
    public decimal? LengthM { get; private set; }

    public decimal? WidthM { get; private set; }

    public decimal? HeightM { get; private set; }

    public bool AllowsHazardous { get; private set; } = true;

    public bool SupportsTemperatureControl { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Typical internal body size (L, W, H in metres) of the default classes, applied when they are first seeded.</summary>
    public static IReadOnlyDictionary<string, (decimal L, decimal W, decimal H)> DefaultDimensions { get; } = new Dictionary<string, (decimal, decimal, decimal)>
    {
        ["ACE"] = (2.1m, 1.4m, 1.4m), ["PICKUP_1_5T"] = (2.6m, 1.5m, 1.5m), ["TRUCK_14FT"] = (4.3m, 2.0m, 2.0m), ["TRUCK_17FT"] = (5.2m, 2.1m, 2.1m),
        ["TRUCK_19FT"] = (5.8m, 2.2m, 2.2m), ["TRUCK_24FT"] = (7.3m, 2.4m, 2.4m), ["TRUCK_32FT_SXL"] = (9.7m, 2.4m, 2.4m), ["TRUCK_32FT_MXL"] = (9.7m, 2.4m, 2.5m),
        ["TRAILER_40FT"] = (12.2m, 2.4m, 2.6m),
    };

    /// <summary>Common Indian truck classes, seeded for every tenant on first use. Tenants can edit or add their own.</summary>
    public static IReadOnlyList<(string Code, string Name, int PayloadKg, decimal? VolumeCbm)> Defaults { get; } =
    [
        ("ACE", "Tata Ace (0.75 T)", 750, 3.5m),
        ("PICKUP_1_5T", "Pickup (1.5 T)", 1500, 7m),
        ("TRUCK_14FT", "14 ft truck (4 T)", 4000, 18m),
        ("TRUCK_17FT", "17 ft truck (6 T)", 6000, 24m),
        ("TRUCK_19FT", "19 ft truck (9 T)", 9000, 30m),
        ("TRUCK_24FT", "24 ft truck (14 T)", 14000, 45m),
        ("TRUCK_32FT_SXL", "32 ft single-axle (7 T)", 7000, 60m),
        ("TRUCK_32FT_MXL", "32 ft multi-axle (16 T)", 16000, 65m),
        ("TRAILER_40FT", "40 ft trailer (28 T)", 28000, 76m),
    ];

    public static Result<VehicleType> Create(
        Guid tenantId, string code, string name, int payloadKg, decimal? volumeCbm,
        decimal? lengthM = null, decimal? widthM = null, decimal? heightM = null, bool allowsHazardous = true, bool supportsTemperatureControl = false)
    {
        var type = new VehicleType { TenantId = tenantId, Code = IndianIdentifiers.Normalise(code), IsActive = true };
        var set = type.Update(name, payloadKg, volumeCbm, true, lengthM, widthM, heightM, allowsHazardous, supportsTemperatureControl);
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (type.Code.Length is 0 or > 32 || !type.Code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return new FieldErrors().Add("code", "Use up to 32 letters, digits or underscores.").ToError();
        }

        return type;
    }

    public Result Update(
        string name, int payloadKg, decimal? volumeCbm, bool isActive,
        decimal? lengthM = null, decimal? widthM = null, decimal? heightM = null, bool allowsHazardous = true, bool supportsTemperatureControl = false)
    {
        var errors = new FieldErrors().Require("name", name, "Name", 100);
        foreach (var (field, value) in new[] { ("lengthM", lengthM), ("widthM", widthM), ("heightM", heightM) })
        {
            if (value is <= 0 or > 30)
            {
                errors.Add(field, "Enter a size in metres between 0 and 30, or leave it blank.");
            }
        }

        if (payloadKg is <= 0 or > 100_000)
        {
            errors.Add("payloadKg", "Payload must be between 1 and 100,000 kg.");
        }

        if (volumeCbm is <= 0)
        {
            errors.Add("volumeCbm", "Volume must be positive.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        Name = name.Trim();
        PayloadKg = payloadKg;
        VolumeCbm = volumeCbm;
        LengthM = lengthM;
        WidthM = widthM;
        HeightM = heightM;
        AllowsHazardous = allowsHazardous;
        SupportsTemperatureControl = supportsTemperatureControl;
        IsActive = isActive;
        return Result.Success();
    }
}

public enum VehicleOwnership
{
    /// <summary>Owned by the transporter.</summary>
    Owned = 1,

    /// <summary>Owner-operator attached to the transporter on a regular basis.</summary>
    Attached = 2,

    /// <summary>Hired from the open market for a trip.</summary>
    Market = 3,
}

public sealed class Vehicle : AggregateRoot, ITenantScoped
{
    private Vehicle()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    /// <summary>Normalised: upper-case, no spaces or dashes (MH12AB1234).</summary>
    public string RegistrationNumber { get; private set; } = null!;

    public Guid VehicleTypeId { get; private set; }

    public VehicleOwnership Ownership { get; private set; }

    public string? Make { get; private set; }

    public int? YearOfManufacture { get; private set; }

    public bool IsActive { get; private set; }

    public FleetAvailability Availability { get; private set; } = FleetAvailability.Available;

    /// <summary>For an available vehicle: the first day it can be used. For one in maintenance or off the road: the day it returns.</summary>
    public DateOnly? AvailableFrom { get; private set; }

    public DateOnly? AvailableTo { get; private set; }

    public string? AvailabilityNote { get; private set; }

    public Result SetAvailability(FleetAvailability availability, DateOnly? from, DateOnly? to, string? note)
    {
        var errors = new FieldErrors();
        if (!Enum.IsDefined(availability))
        {
            errors.Add("availability", "Choose a status.");
        }

        if (from is { } f && to is { } t && t < f)
        {
            errors.Add("availableTo", "The end of availability cannot be before its start.");
        }

        if (note?.Length > 200)
        {
            errors.Add("availabilityNote", "The note can be at most 200 characters.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        Availability = availability;
        AvailableFrom = from;
        AvailableTo = to;
        AvailabilityNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Result.Success();
    }

    public static Result<Vehicle> Create(Guid tenantId, Guid transporterId, string registrationNumber, Guid vehicleTypeId, VehicleOwnership ownership, string? make, int? year)
    {
        var vehicle = new Vehicle { TenantId = tenantId, TransporterId = transporterId, IsActive = true };
        var errors = new FieldErrors();
        var registration = IndianIdentifiers.NormaliseVehicleRegistration(registrationNumber);
        if (!IndianIdentifiers.IsValidVehicleRegistration(registration))
        {
            errors.Add("registrationNumber", "Enter a valid registration number, e.g. MH12AB1234.");
        }

        var applied = vehicle.Apply(vehicleTypeId, ownership, make, year, errors);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        vehicle.RegistrationNumber = registration;
        return vehicle;
    }

    public Result Update(Guid vehicleTypeId, VehicleOwnership ownership, string? make, int? year, bool isActive)
    {
        var applied = Apply(vehicleTypeId, ownership, make, year, new FieldErrors());
        if (applied.IsSuccess)
        {
            IsActive = isActive;
        }

        return applied;
    }

    private Result Apply(Guid vehicleTypeId, VehicleOwnership ownership, string? make, int? year, FieldErrors errors)
    {
        if (vehicleTypeId == Guid.Empty)
        {
            errors.Add("vehicleTypeId", "Choose a vehicle type.");
        }

        if (!Enum.IsDefined(ownership))
        {
            errors.Add("ownership", "Choose an ownership type.");
        }

        if (year is { } y && (y < 1980 || y > DateTime.UtcNow.Year + 1))
        {
            errors.Add("yearOfManufacture", "Enter a realistic year of manufacture.");
        }

        if (make?.Length > 100)
        {
            errors.Add("make", "Make must be at most 100 characters.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        VehicleTypeId = vehicleTypeId;
        Ownership = ownership;
        Make = string.IsNullOrWhiteSpace(make) ? null : make.Trim();
        YearOfManufacture = year;
        return Result.Success();
    }
}

public sealed class Driver : AggregateRoot, ITenantScoped
{
    private Driver()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string FullName { get; private set; } = null!;

    public string Phone { get; private set; } = null!;

    public string? LicenseNumber { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<Driver> Create(Guid tenantId, Guid transporterId, string fullName, string phone, string? licenseNumber)
    {
        var driver = new Driver { TenantId = tenantId, TransporterId = transporterId, IsActive = true };
        var applied = driver.Update(fullName, phone, licenseNumber, true);
        return applied.IsFailure ? applied.Error : driver;
    }

    public Result Update(string fullName, string phone, string? licenseNumber, bool isActive)
    {
        var errors = new FieldErrors().Require("fullName", fullName, "Name", 150);
        if (!IndianIdentifiers.IsValidMobile(phone))
        {
            errors.Add("phone", "Enter a valid 10-digit mobile number.");
        }

        var license = string.IsNullOrWhiteSpace(licenseNumber) ? null : new string(IndianIdentifiers.Normalise(licenseNumber).Where(char.IsLetterOrDigit).ToArray());
        if (license is { Length: < 8 or > 20 })
        {
            errors.Add("licenseNumber", "Enter the driving licence number as printed on the licence.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        FullName = fullName.Trim();
        Phone = IndianIdentifiers.NormaliseMobile(phone);
        LicenseNumber = license;
        IsActive = isActive;
        return Result.Success();
    }
}
