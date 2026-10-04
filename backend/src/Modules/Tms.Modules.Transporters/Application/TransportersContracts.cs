using FluentValidation;
using Tms.SharedKernel.Contracts;
using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Application;

public sealed record AddressDto(string Line1, string? Line2, string City, string State, string Pincode);

public sealed record BankDto(string AccountHolder, string AccountNumberMasked, string Ifsc, string BankName);

public sealed record TransporterSummaryDto(
    Guid Id,
    string Code,
    string LegalName,
    string? TradeName,
    string City,
    string State,
    TransporterStatus Status,
    IReadOnlyList<string> ServiceModes,
    string Phone);

public sealed record TransporterDto(
    Guid Id,
    string Code,
    TransporterStatus Status,
    string LegalName,
    string? TradeName,
    string Pan,
    string? Gstin,
    string ContactPerson,
    string Phone,
    string Email,
    AddressDto Address,
    IReadOnlyList<string> ServiceModes,
    BankDto? Bank,
    Guid? ApprovalRequestId,
    string? SuspensionReason,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset CreatedAt,
    long Version,
    IReadOnlyList<string> MissingForSubmission);

public sealed record TransporterLookupDto(Guid Id, string Code, string LegalName);

public sealed record ListTransportersQuery(string? Search, TransporterStatus? Status, int Page = 1, int PageSize = 25);

/// <param name="ServiceModes">Any of <c>Ftl</c>, <c>Ptl</c>, <c>Dedicated</c>.</param>
/// <param name="Version">Required when updating (optimistic concurrency); ignored on create.</param>
public sealed record SaveTransporterRequest(
    string LegalName,
    string? TradeName,
    string Pan,
    string? Gstin,
    string ContactPerson,
    string Phone,
    string Email,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string Pincode,
    IReadOnlyList<string> ServiceModes,
    long? Version);

public sealed record SaveBankRequest(string AccountHolder, string AccountNumber, string Ifsc, string BankName, long Version);

public sealed record SuspendRequest(string Reason);

public sealed record VehicleTypeDto(
    Guid Id, string Code, string Name, int PayloadKg, decimal? VolumeCbm, bool IsActive, long Version,
    decimal? LengthM = null, decimal? WidthM = null, decimal? HeightM = null, bool AllowsHazardous = true, bool SupportsTemperatureControl = false);

public sealed record SaveVehicleTypeRequest(
    string Code, string Name, int PayloadKg, decimal? VolumeCbm, bool IsActive, long? Version,
    decimal? LengthM = null, decimal? WidthM = null, decimal? HeightM = null, bool AllowsHazardous = true, bool SupportsTemperatureControl = false);

public sealed record ComplianceDto(ComplianceStatus Status, IReadOnlyList<string> Issues);

public sealed record VehicleDto(
    Guid Id,
    Guid TransporterId,
    string RegistrationNumber,
    Guid VehicleTypeId,
    string VehicleTypeName,
    int PayloadKg,
    VehicleOwnership Ownership,
    string? Make,
    int? YearOfManufacture,
    bool IsActive,
    ComplianceDto Compliance,
    long Version,
    FleetAvailability Availability = FleetAvailability.Available,
    DateOnly? AvailableFrom = null,
    DateOnly? AvailableTo = null,
    string? AvailabilityNote = null);

public sealed record SaveVehicleRequest(
    string RegistrationNumber,
    Guid VehicleTypeId,
    VehicleOwnership Ownership,
    string? Make,
    int? YearOfManufacture,
    bool IsActive,
    long? Version,
    FleetAvailability Availability = FleetAvailability.Available,
    DateOnly? AvailableFrom = null,
    DateOnly? AvailableTo = null,
    string? AvailabilityNote = null);

public sealed record DriverDto(
    Guid Id,
    Guid TransporterId,
    string FullName,
    string Phone,
    string? LicenseNumber,
    bool IsActive,
    ComplianceDto Compliance,
    long Version);

public sealed record SaveDriverRequest(string FullName, string Phone, string? LicenseNumber, bool IsActive, long? Version);

public sealed record DocumentDto(
    Guid Id,
    Guid TransporterId,
    OwnerKind OwnerKind,
    Guid OwnerId,
    DocumentKind Kind,
    string KindLabel,
    string? Number,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string FileName,
    string ContentType,
    long SizeBytes,
    ExpiryStatus ExpiryStatus,
    bool IsCurrent,
    DateTimeOffset UploadedAt);

public sealed record ComplianceItemDto(DocumentDto Document, string OwnerLabel, string TransporterName);

public sealed record ComplianceReportQuery(int WithinDays = 30, int Page = 1, int PageSize = 50);

internal sealed class SaveTransporterRequestValidator : AbstractValidator<SaveTransporterRequest>
{
    public SaveTransporterRequestValidator()
    {
        // Format rules (PAN, GSTIN, phone…) live in the domain so they apply everywhere; this only guards the shape.
        RuleFor(x => x.LegalName).NotNull();
        RuleFor(x => x.Pan).NotNull();
        RuleFor(x => x.Phone).NotNull();
        RuleFor(x => x.Email).NotNull();
        RuleFor(x => x.ServiceModes).NotNull()
            .Must(m => m is null || m.All(v => Enum.TryParse<ServiceModes>(v, true, out var parsed) && parsed != ServiceModes.None))
            .WithMessage("Service modes must be any of Ftl, Ptl, Dedicated.");
    }
}

internal sealed class SaveBankRequestValidator : AbstractValidator<SaveBankRequest>
{
    public SaveBankRequestValidator()
    {
        RuleFor(x => x.AccountHolder).NotNull();
        RuleFor(x => x.AccountNumber).NotNull();
        RuleFor(x => x.Ifsc).NotNull();
        RuleFor(x => x.BankName).NotNull();
    }
}

internal sealed class SuspendRequestValidator : AbstractValidator<SuspendRequest>
{
    public SuspendRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class SaveVehicleTypeRequestValidator : AbstractValidator<SaveVehicleTypeRequest>
{
    public SaveVehicleTypeRequestValidator()
    {
        RuleFor(x => x.Code).NotNull();
        RuleFor(x => x.Name).NotNull();
    }
}

internal sealed class SaveVehicleRequestValidator : AbstractValidator<SaveVehicleRequest>
{
    public SaveVehicleRequestValidator() => RuleFor(x => x.RegistrationNumber).NotNull();
}

internal sealed class SaveDriverRequestValidator : AbstractValidator<SaveDriverRequest>
{
    public SaveDriverRequestValidator()
    {
        RuleFor(x => x.FullName).NotNull();
        RuleFor(x => x.Phone).NotNull();
    }
}
