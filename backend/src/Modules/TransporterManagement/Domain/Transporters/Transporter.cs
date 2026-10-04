using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Transporters;

public class Transporter
{
    public long Id { get; set; }
    public string TransporterCode { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public long? TransporterTypeId { get; set; }
    public string? CompanyType { get; set; }
    public string? Pan { get; set; }
    public string? Gstin { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? PrimaryContactPhone { get; set; }
    public string? Website { get; set; }
    public TransporterStatus Status { get; set; } = TransporterStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public class TransporterContact
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string ContactType { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TransporterBranch
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TransporterLane
{
    public long Id { get; set; }
    public long TransporterId { get; set; }

    /// <summary>Reference to a Location master owned outside this module. Not a foreign key.</summary>
    public long OriginLocationReference { get; set; }

    /// <summary>Reference to a Location master owned outside this module. Not a foreign key.</summary>
    public long DestinationLocationReference { get; set; }

    public string ServiceType { get; set; } = string.Empty;
    public long? VehicleTypeReference { get; set; }
    public int? TransitSlaMinutes { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
}

public class TransporterCapability
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public long CapabilityTypeId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
}

public class TransporterDocument
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public long? VehicleId { get; set; }

    /// <summary>Set for driver-level documents such as a driving licence.</summary>
    public long? DriverId { get; set; }
    public long DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? FileReference { get; set; }
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public DocumentVerificationStatus VerificationStatus { get; set; } = DocumentVerificationStatus.Pending;
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? Remarks { get; set; }
}

public class TransporterVehicle
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public long VehicleTypeReference { get; set; }
    public decimal PayloadCapacityKg { get; set; }
    public decimal? UsableVolumeM3 { get; set; }
    public decimal? LengthM { get; set; }
    public decimal? WidthM { get; set; }
    public decimal? HeightM { get; set; }
    public VehicleOwnershipType OwnershipType { get; set; } = VehicleOwnershipType.Owned;
    public VehicleAvailabilityStatus AvailabilityStatus { get; set; } = VehicleAvailabilityStatus.Available;
    public long? CurrentLocationReference { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
}

public class TransporterUser
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime? LastLoginAt { get; set; }
}

public class TransporterRate
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public long OriginLocationReference { get; set; }
    public long DestinationLocationReference { get; set; }
    public long? VehicleTypeReference { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public RateType RateType { get; set; }
    public decimal RateValue { get; set; }
    public decimal? MinimumCharge { get; set; }
    public decimal? FuelSurcharge { get; set; }
    public decimal? TollAmount { get; set; }
    public decimal? OtherCharges { get; set; }
    public string Currency { get; set; } = "INR";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
}

/// <summary>A driver employed or engaged by a transporter. Driver-level documents (such as a licence) attach here.</summary>
public class TransporterDriver
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string LicenceNumber { get; set; } = string.Empty;
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
