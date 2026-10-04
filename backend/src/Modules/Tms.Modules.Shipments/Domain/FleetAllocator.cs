using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

public sealed record FleetSuggestion(AssignedVehicle? Vehicle, AssignedDriver? Driver, string? Problem)
{
    public bool Available => Problem is null;
}

/// <summary>
/// Picks a real vehicle and driver from a transporter's fleet for a day: active, in service that day, with papers in order, and not
/// already given to another trip of the same plan. It never double-books and it says why when nothing is free.
/// </summary>
public sealed class FleetAllocator(IFleetDirectory? fleet, ITransporterDirectory? transporters)
{
    private readonly HashSet<Guid> _usedVehicles = [];
    private readonly HashSet<Guid> _usedDrivers = [];
    private readonly Dictionary<Guid, (IReadOnlyList<FleetVehicle> Vehicles, IReadOnlyList<FleetDriver> Drivers)> _fleets = [];
    private readonly Dictionary<Guid, AssignedTransporter?> _transporterCache = [];

    public bool Enabled => fleet is not null;

    public bool IsTaken(Guid vehicleId) => _usedVehicles.Contains(vehicleId);

    public bool DriverTaken(Guid driverId) => _usedDrivers.Contains(driverId);

    /// <summary>Marks vehicles and drivers already assigned elsewhere in the plan (locked or untouched trips) as taken.</summary>
    public void Reserve(IEnumerable<PlannedVehicle> existing)
    {
        foreach (var v in existing)
        {
            Reserve(v.AssignedVehicle, v.AssignedDriver);
        }
    }

    public void Reserve(AssignedVehicle? vehicle, AssignedDriver? driver)
    {
        if (vehicle is not null)
        {
            _usedVehicles.Add(vehicle.Id);
        }

        if (driver is not null)
        {
            _usedDrivers.Add(driver.Id);
        }
    }

    public async Task<AssignedTransporter?> TransporterAsync(Guid id, CancellationToken cancellationToken)
    {
        if (transporters is null)
        {
            return null;
        }

        if (!_transporterCache.TryGetValue(id, out var found))
        {
            var info = (await transporters.GetAsync([id], cancellationToken)).GetValueOrDefault(id);
            found = info is null ? null : new AssignedTransporter(info.Id, info.Code, info.LegalName, info.ContactPerson, info.Phone, info.Email, info.City);
            _transporterCache[id] = found;
        }

        return found;
    }

    /// <param name="requiredVehicleId">When a planner has locked the assignment, only that vehicle (and driver) will do.</param>
    public async Task<FleetSuggestion> SuggestAsync(
        Guid transporterId, Guid vehicleTypeId, string typeName, DateOnly date, Guid? requiredVehicleId, Guid? requiredDriverId, CancellationToken cancellationToken)
    {
        if (fleet is null)
        {
            return new FleetSuggestion(null, null, null); // no fleet data in this context: nothing to check
        }

        if (!_fleets.TryGetValue(transporterId, out var all))
        {
            all = (await fleet.ListVehiclesAsync(transporterId, cancellationToken), await fleet.ListDriversAsync(transporterId, cancellationToken));
            _fleets[transporterId] = all;
        }

        var ofType = all.Vehicles.Where(v => v.VehicleTypeId == vehicleTypeId).ToList();
        if (ofType.Count == 0)
        {
            return new FleetSuggestion(null, null, $"This transporter has no {typeName} registered.");
        }

        var problems = new List<string>();
        var usable = new List<FleetVehicle>();
        foreach (var v in ofType.OrderBy(v => v.RegistrationNumber, StringComparer.Ordinal))
        {
            if (requiredVehicleId is { } wanted && v.Id != wanted)
            {
                continue;
            }

            if (!v.IsAvailableOn(date))
            {
                problems.Add($"{v.RegistrationNumber} is {v.WhyUnavailableOn(date)}");
            }
            else if (v.Compliance == FleetCompliance.NonCompliant)
            {
                problems.Add($"{v.RegistrationNumber}: {string.Join("; ", v.Issues)}");
            }
            else if (_usedVehicles.Contains(v.Id))
            {
                problems.Add($"{v.RegistrationNumber} is already on another trip in this plan");
            }
            else
            {
                usable.Add(v);
            }
        }

        if (requiredVehicleId is not null && usable.Count == 0 && problems.Count == 0)
        {
            problems.Add("the vehicle chosen for this trip is no longer registered with this transporter");
        }

        var vehicle = usable.OrderBy(v => v.Compliance).FirstOrDefault(); // compliant before expiring-soon, then by registration
        if (vehicle is null)
        {
            return new FleetSuggestion(null, null, $"No {typeName} is free on {date:dd MMM}: {string.Join("; ", problems)}.");
        }

        var drivers = all.Drivers
            .Where(d => d.IsActive && (requiredDriverId is null || d.Id == requiredDriverId) && !_usedDrivers.Contains(d.Id) && d.Compliance != FleetCompliance.NonCompliant)
            .OrderBy(d => d.Compliance).ThenBy(d => d.FullName, StringComparer.Ordinal)
            .ToList();
        var driver = drivers.FirstOrDefault();
        if (driver is null)
        {
            var why = all.Drivers.Count == 0 ? "has no drivers registered" : "has no driver free with a valid licence";
            return new FleetSuggestion(ToAssigned(vehicle), null, $"This transporter {why} on {date:dd MMM}.");
        }

        return new FleetSuggestion(ToAssigned(vehicle), new AssignedDriver(driver.Id, driver.FullName, driver.Phone, driver.LicenseNumber, driver.Compliance.ToString(), driver.Issues), null);
    }

    private static AssignedVehicle ToAssigned(FleetVehicle v) => new(v.Id, v.RegistrationNumber, v.VehicleTypeName, v.PayloadKg, v.Compliance.ToString(), v.Issues);
}
