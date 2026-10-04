using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Transporters;

// ---------- Read models ----------

public sealed record TransporterListItem(
    long Id,
    string TransporterCode,
    string LegalName,
    string? TradeName,
    string? TransporterType,
    TransporterStatus Status,
    string? City,
    string? State,
    string? PrimaryContactName,
    string? PrimaryContactPhone,
    int ActiveVehicles,
    int ActiveLanes);

public sealed record TransporterDetail(
    long Id,
    string TransporterCode,
    string LegalName,
    string? TradeName,
    long? TransporterTypeId,
    string? TransporterType,
    string? CompanyType,
    string? Pan,
    string? Gstin,
    string? RegistrationNumber,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? PrimaryContactName,
    string? PrimaryContactEmail,
    string? PrimaryContactPhone,
    string? Website,
    TransporterStatus Status,
    int ActiveVehicles,
    int ActiveLanes,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<BranchDto> Branches,
    IReadOnlyList<CapabilityDto> Capabilities,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ContactDto(long Id, string Name, string? Designation, string? Email, string? Phone, string ContactType, bool IsPrimary, bool IsActive);

public sealed record BranchDto(long Id, string BranchCode, string BranchName, string? Address, string? City, string? State, double? Latitude, double? Longitude, string? ContactName, string? ContactPhone, bool IsActive);

public sealed record VehicleDto(
    long Id,
    long TransporterId,
    string RegistrationNumber,
    long VehicleTypeReference,
    decimal PayloadCapacityKg,
    decimal? UsableVolumeM3,
    decimal? LengthM,
    decimal? WidthM,
    decimal? HeightM,
    VehicleOwnershipType OwnershipType,
    VehicleAvailabilityStatus AvailabilityStatus,
    long? CurrentLocationReference,
    RecordStatus Status);

public sealed record LaneDto(
    long Id,
    long TransporterId,
    long OriginLocationReference,
    long DestinationLocationReference,
    string ServiceType,
    long? VehicleTypeReference,
    int? TransitSlaMinutes,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    RecordStatus Status);

public sealed record CapabilityDto(long Id, long CapabilityTypeId, string Code, string Name, DateTime EffectiveFrom, DateTime? EffectiveTo, RecordStatus Status);

public sealed record LookupDto(long Id, string Code, string Name);

// ---------- Write requests ----------

public sealed record CreateTransporterRequest(
    string TransporterCode,
    string LegalName,
    string? TradeName,
    long? TransporterTypeId,
    string? CompanyType,
    string? Pan,
    string? Gstin,
    string? RegistrationNumber,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? PrimaryContactName,
    string? PrimaryContactEmail,
    string? PrimaryContactPhone,
    string? Website);

/// <summary>Master-data update. The transporter code is immutable once issued.</summary>
public sealed record UpdateTransporterRequest(
    string LegalName,
    string? TradeName,
    long? TransporterTypeId,
    string? CompanyType,
    string? Pan,
    string? Gstin,
    string? RegistrationNumber,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? PrimaryContactName,
    string? PrimaryContactEmail,
    string? PrimaryContactPhone,
    string? Website);

public sealed record SaveContactRequest(
    string Name,
    string? Designation,
    string? Email,
    string? Phone,
    string ContactType,
    bool IsPrimary,
    bool IsActive = true);

public sealed record SaveBranchRequest(
    string BranchCode,
    string BranchName,
    string? Address,
    string? City,
    string? State,
    double? Latitude,
    double? Longitude,
    string? ContactName,
    string? ContactPhone,
    bool IsActive = true);

public sealed record SaveVehicleRequest(
    string RegistrationNumber,
    long VehicleTypeReference,
    decimal PayloadCapacityKg,
    decimal? UsableVolumeM3,
    decimal? LengthM,
    decimal? WidthM,
    decimal? HeightM,
    VehicleOwnershipType OwnershipType,
    VehicleAvailabilityStatus AvailabilityStatus,
    long? CurrentLocationReference,
    RecordStatus Status = RecordStatus.Active);

public sealed record SaveLaneRequest(
    long OriginLocationReference,
    long DestinationLocationReference,
    string ServiceType,
    long? VehicleTypeReference,
    int? TransitSlaMinutes,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    RecordStatus Status = RecordStatus.Active);

public sealed record AddCapabilityRequest(long CapabilityTypeId, DateTime EffectiveFrom, DateTime? EffectiveTo = null);

// ---------- Search criteria ----------

public sealed record TransporterSearch(
    string? Search = null,
    TransporterStatus? Status = null,
    long? TransporterTypeId = null,
    string? City = null,
    int Page = 1,
    int PageSize = 25);

public sealed record VehicleSearch(
    VehicleAvailabilityStatus? AvailabilityStatus = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record LaneSearch(
    long? OriginLocationReference = null,
    long? DestinationLocationReference = null,
    string? ServiceType = null,
    RecordStatus? Status = null,
    int Page = 1,
    int PageSize = 25);
