using System.Text.RegularExpressions;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Placement;

/// <summary>Checks that a vehicle a transporter nominates is usable for a specific load. Shared by tender and placement flows.</summary>
public static class VehicleSelection
{
    public static string Normalise(string registration) =>
        Regex.Replace(registration, "[\\s-]+", string.Empty).ToUpperInvariant();

    /// <summary>Returns the fleet vehicle, or throws when it is missing, unavailable, the wrong type or too small.</summary>
    public static TransporterVehicle Require(TransporterVehicle? fleet, string registration, long? requiredType, decimal weightKg)
    {
        if (fleet is null || fleet.Status != RecordStatus.Active
            || fleet.AvailabilityStatus is VehicleAvailabilityStatus.Maintenance or VehicleAvailabilityStatus.Blocked or VehicleAvailabilityStatus.Inactive)
        {
            throw new BusinessRuleException($"Vehicle {registration} is not an available vehicle in this transporter's fleet.", "VEHICLE_NOT_IN_FLEET");
        }

        if (requiredType is { } type && fleet.VehicleTypeReference != type)
        {
            throw new BusinessRuleException("The vehicle type does not match the tender requirement.", "VEHICLE_TYPE_MISMATCH");
        }

        if (fleet.PayloadCapacityKg < weightKg)
        {
            throw new BusinessRuleException($"Vehicle capacity of {fleet.PayloadCapacityKg:0.#} kg is below the load weight of {weightKg:0.#} kg.", "VEHICLE_CAPACITY_INSUFFICIENT");
        }

        return fleet;
    }
}
