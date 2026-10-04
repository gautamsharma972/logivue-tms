using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Planning;

namespace LogiVue.Tms.TransporterManagement.Application.Validation;

public sealed class TransporterSelectionRequestValidator : AbstractValidator<TransporterSelectionRequest>
{
    public TransporterSelectionRequestValidator()
    {
        RuleFor(x => x.OriginLocationId).GreaterThan(0);
        RuleFor(x => x.DestinationLocationId).GreaterThan(0)
            .NotEqual(x => x.OriginLocationId).WithMessage("Destination must differ from origin.");
        RuleFor(x => x.ServiceType).NotEmpty().Must(ValidationPatterns.IsServiceType).WithMessage("Use 2-30 letters or underscores.");
        RuleFor(x => x.VehicleTypeId).GreaterThan(0).When(x => x.VehicleTypeId.HasValue);
        RuleFor(x => x.RequiredWeightKg).GreaterThan(0).LessThanOrEqualTo(40000);
        RuleFor(x => x.RequiredVolumeM3).GreaterThan(0).LessThanOrEqualTo(500).When(x => x.RequiredVolumeM3.HasValue);
        RuleFor(x => x.DistanceKm).GreaterThan(0).LessThanOrEqualTo(10000).When(x => x.DistanceKm.HasValue);
        RuleFor(x => x.RequiredCapabilities).Must(c => c.Count <= 10).WithMessage("Request at most 10 capabilities.");
        RuleFor(x => x.DeliverBy).GreaterThan(x => x.PickupBy)
            .When(x => x.PickupBy.HasValue && x.DeliverBy.HasValue)
            .WithMessage("Delivery deadline must be after pickup.");
    }
}

public sealed class PlanningRuleRequestValidator : AbstractValidator<PlanningRuleRequest>
{
    public PlanningRuleRequestValidator()
    {
        RuleFor(x => x.RuleType).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required for a planning rule.").MaximumLength(500);
        RuleFor(x => x.LaneReference).GreaterThan(0).When(x => x.LaneReference.HasValue);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo.HasValue)
            .WithMessage("Effective-to must be on or after effective-from.");
    }
}
