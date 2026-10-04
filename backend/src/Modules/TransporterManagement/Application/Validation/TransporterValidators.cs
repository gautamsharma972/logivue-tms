using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Validation;

public sealed class CreateTransporterRequestValidator : AbstractValidator<CreateTransporterRequest>
{
    public CreateTransporterRequestValidator()
    {
        RuleFor(x => x.TransporterCode).NotEmpty()
            .Must(ValidationPatterns.IsTransporterCode).WithMessage("Use 3-30 letters, digits or hyphens.");
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.CompanyType).MaximumLength(50);
        RuleFor(x => x.Pan).Must(ValidationPatterns.IsPan).WithMessage("PAN must match AAAAA9999A.").When(x => !string.IsNullOrWhiteSpace(x.Pan));
        RuleFor(x => x.Gstin).Must(ValidationPatterns.IsGstin).WithMessage("GSTIN must be a valid 15-character GSTIN.").When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.RegistrationNumber).MaximumLength(60);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.PrimaryContactName).MaximumLength(150);
        RuleFor(x => x.PrimaryContactEmail).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.PrimaryContactEmail));
        RuleFor(x => x.PrimaryContactPhone).Must(ValidationPatterns.IsPhone).WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.PrimaryContactPhone));
        RuleFor(x => x.Website).MaximumLength(200);
        RuleFor(x => x.TransporterTypeId).GreaterThan(0).When(x => x.TransporterTypeId.HasValue);
    }
}

public sealed class UpdateTransporterRequestValidator : AbstractValidator<UpdateTransporterRequest>
{
    public UpdateTransporterRequestValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.CompanyType).MaximumLength(50);
        RuleFor(x => x.Pan).Must(ValidationPatterns.IsPan).WithMessage("PAN must match AAAAA9999A.").When(x => !string.IsNullOrWhiteSpace(x.Pan));
        RuleFor(x => x.Gstin).Must(ValidationPatterns.IsGstin).WithMessage("GSTIN must be a valid 15-character GSTIN.").When(x => !string.IsNullOrWhiteSpace(x.Gstin));
        RuleFor(x => x.RegistrationNumber).MaximumLength(60);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.PrimaryContactName).MaximumLength(150);
        RuleFor(x => x.PrimaryContactEmail).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.PrimaryContactEmail));
        RuleFor(x => x.PrimaryContactPhone).Must(ValidationPatterns.IsPhone).WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.PrimaryContactPhone));
        RuleFor(x => x.Website).MaximumLength(200);
        RuleFor(x => x.TransporterTypeId).GreaterThan(0).When(x => x.TransporterTypeId.HasValue);
    }
}

public sealed class SaveContactRequestValidator : AbstractValidator<SaveContactRequest>
{
    public SaveContactRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Designation).MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Must(ValidationPatterns.IsPhone).WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.ContactType).NotEmpty().MaximumLength(50);
    }
}

public sealed class SaveBranchRequestValidator : AbstractValidator<SaveBranchRequest>
{
    public SaveBranchRequestValidator()
    {
        RuleFor(x => x.BranchCode).NotEmpty().Must(ValidationPatterns.IsBranchCode).WithMessage("Use 2-30 letters, digits or hyphens.");
        RuleFor(x => x.BranchName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.ContactName).MaximumLength(150);
        RuleFor(x => x.ContactPhone).Must(ValidationPatterns.IsPhone).WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
        RuleFor(x => x).Must(x => x.Latitude.HasValue == x.Longitude.HasValue)
            .WithMessage("Provide both latitude and longitude, or neither.");
    }
}

public sealed class SaveVehicleRequestValidator : AbstractValidator<SaveVehicleRequest>
{
    public SaveVehicleRequestValidator()
    {
        RuleFor(x => x.RegistrationNumber).NotEmpty().Must(ValidationPatterns.IsRegistrationNumber)
            .WithMessage("Use 4-20 letters, digits, spaces or hyphens.");
        RuleFor(x => x.VehicleTypeReference).GreaterThan(0);
        RuleFor(x => x.PayloadCapacityKg).GreaterThan(0).LessThanOrEqualTo(40000);
        RuleFor(x => x.UsableVolumeM3).GreaterThan(0).LessThanOrEqualTo(500).When(x => x.UsableVolumeM3.HasValue);
        RuleFor(x => x.LengthM).GreaterThan(0).LessThanOrEqualTo(30).When(x => x.LengthM.HasValue);
        RuleFor(x => x.WidthM).GreaterThan(0).LessThanOrEqualTo(5).When(x => x.WidthM.HasValue);
        RuleFor(x => x.HeightM).GreaterThan(0).LessThanOrEqualTo(6).When(x => x.HeightM.HasValue);
        RuleFor(x => x.CurrentLocationReference).GreaterThan(0).When(x => x.CurrentLocationReference.HasValue);
        RuleFor(x => x.OwnershipType).IsInEnum();
        RuleFor(x => x.AvailabilityStatus).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class SaveLaneRequestValidator : AbstractValidator<SaveLaneRequest>
{
    public SaveLaneRequestValidator()
    {
        RuleFor(x => x.OriginLocationReference).GreaterThan(0);
        RuleFor(x => x.DestinationLocationReference).GreaterThan(0)
            .NotEqual(x => x.OriginLocationReference).WithMessage("Destination must differ from origin.");
        RuleFor(x => x.ServiceType).NotEmpty().Must(ValidationPatterns.IsServiceType)
            .WithMessage("Use 2-30 letters or underscores.");
        RuleFor(x => x.VehicleTypeReference).GreaterThan(0).When(x => x.VehicleTypeReference.HasValue);
        RuleFor(x => x.TransitSlaMinutes).InclusiveBetween(1, 20160).When(x => x.TransitSlaMinutes.HasValue);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo.HasValue)
            .WithMessage("Effective-to must be on or after effective-from.");
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class AddCapabilityRequestValidator : AbstractValidator<AddCapabilityRequest>
{
    public AddCapabilityRequestValidator()
    {
        RuleFor(x => x.CapabilityTypeId).GreaterThan(0);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo.HasValue)
            .WithMessage("Effective-to must be on or after effective-from.");
    }
}
