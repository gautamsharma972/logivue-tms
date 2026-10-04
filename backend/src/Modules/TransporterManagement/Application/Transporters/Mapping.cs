using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Transporters;

internal static class Mapping
{
    public static ContactDto ToDto(this TransporterContact c) =>
        new(c.Id, c.Name, c.Designation, c.Email, c.Phone, c.ContactType, c.IsPrimary, c.IsActive);

    public static BranchDto ToDto(this TransporterBranch b) =>
        new(b.Id, b.BranchCode, b.BranchName, b.Address, b.City, b.State, b.Latitude, b.Longitude, b.ContactName, b.ContactPhone, b.IsActive);

    public static VehicleDto ToDto(this TransporterVehicle v) =>
        new(v.Id, v.TransporterId, v.RegistrationNumber, v.VehicleTypeReference, v.PayloadCapacityKg, v.UsableVolumeM3,
            v.LengthM, v.WidthM, v.HeightM, v.OwnershipType, v.AvailabilityStatus, v.CurrentLocationReference, v.Status);

    public static LaneDto ToDto(this TransporterLane l) =>
        new(l.Id, l.TransporterId, l.OriginLocationReference, l.DestinationLocationReference, l.ServiceType,
            l.VehicleTypeReference, l.TransitSlaMinutes, l.EffectiveFrom, l.EffectiveTo, l.Status);

    public static CapabilityDto ToDto(this TransporterCapability c, string code, string name) =>
        new(c.Id, c.CapabilityTypeId, code, name, c.EffectiveFrom, c.EffectiveTo, c.Status);

    /// <summary>Trims free text and maps blank values to null, so optional fields store consistently.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? CleanUpper(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
