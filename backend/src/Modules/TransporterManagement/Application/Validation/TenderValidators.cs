using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Tendering;

namespace LogiVue.Tms.TransporterManagement.Application.Validation;

public sealed class CreateTenderRequestValidator : AbstractValidator<CreateTenderRequest>
{
    public CreateTenderRequestValidator()
    {
        RuleFor(x => x.LoadReference).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OriginLocationReference).GreaterThan(0);
        RuleFor(x => x.DestinationLocationReference).GreaterThan(0)
            .NotEqual(x => x.OriginLocationReference).WithMessage("Destination must differ from origin.");
        RuleFor(x => x.ServiceType).NotEmpty().Must(ValidationPatterns.IsServiceType).WithMessage("Use 2-30 letters or underscores.");
        RuleFor(x => x.VehicleTypeReference).GreaterThan(0).When(x => x.VehicleTypeReference.HasValue);
        RuleFor(x => x.WeightKg).GreaterThan(0).LessThanOrEqualTo(40000);
        RuleFor(x => x.VolumeM3).GreaterThan(0).LessThanOrEqualTo(500).When(x => x.VolumeM3.HasValue);
        RuleFor(x => x.OfferedRate).GreaterThan(0).When(x => x.OfferedRate.HasValue);
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrWhiteSpace(x.Currency));
        RuleFor(x => x.TransporterIds).NotEmpty().WithMessage("Select at least one transporter.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each transporter may be invited once.")
            .Must(ids => ids.Count <= 20).WithMessage("A tender may invite at most 20 transporters.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class RejectTenderRequestValidator : AbstractValidator<RejectTenderRequest>
{
    public RejectTenderRequestValidator()
    {
        RuleFor(x => x.ReasonCode).NotEmpty().WithMessage("A rejection reason is required.").MaximumLength(40);
        RuleFor(x => x.Comments).MaximumLength(1000);
    }
}

public sealed class CounterOfferRequestValidator : AbstractValidator<CounterOfferRequest>
{
    public CounterOfferRequestValidator()
    {
        RuleFor(x => x.Rate).GreaterThan(0).LessThanOrEqualTo(10_000_000);
        RuleFor(x => x.Comments).MaximumLength(1000);
    }
}

public sealed class VehicleAssignmentRequestValidator : AbstractValidator<VehicleAssignmentRequest>
{
    public VehicleAssignmentRequestValidator()
    {
        RuleFor(x => x.RegistrationNumber).NotEmpty().Must(ValidationPatterns.IsRegistrationNumber)
            .WithMessage("Use 4-20 letters, digits, spaces or hyphens.");
        RuleFor(x => x.DriverName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DriverMobile).NotEmpty().Must(ValidationPatterns.IsPhone).WithMessage("Enter a valid mobile number.");
    }
}
