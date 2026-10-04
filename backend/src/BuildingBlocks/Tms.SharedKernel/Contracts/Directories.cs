namespace Tms.SharedKernel.Contracts;

public sealed record TransporterInfo(Guid Id, string Code, string LegalName, bool IsActive, string? ContactPerson = null, string? Phone = null, string? Email = null, string? City = null);

/// <param name="LengthM">Internal dimensions in metres; null when not recorded (then no length check is made).</param>
/// <param name="AllowsHazardous">Whether dangerous goods may be carried in this vehicle type.</param>
/// <param name="SupportsTemperatureControl">Refrigerated / temperature-controlled body.</param>
public sealed record VehicleTypeInfo(
    Guid Id, string Code, string Name, int PayloadKg, decimal? VolumeCbm, bool IsActive,
    decimal? LengthM = null, decimal? WidthM = null, decimal? HeightM = null, bool AllowsHazardous = true, bool SupportsTemperatureControl = false);

public sealed record UserContact(Guid Id, string FullName, string Email);

/// <summary>Read-only vehicle-type lookup for modules that price or plan by vehicle type (implemented by Transporters).</summary>
public interface IVehicleTypeDirectory
{
    Task<IReadOnlyDictionary<Guid, VehicleTypeInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Active vehicle types for pickers, smallest first. Provides the standard set the first time a tenant needs it.</summary>
    Task<IReadOnlyList<VehicleTypeInfo>> ListActiveAsync(CancellationToken cancellationToken = default);
}
