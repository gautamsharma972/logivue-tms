using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Documents;
using LogiVue.Tms.TransporterManagement.Application.Execution;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Pod;

namespace LogiVue.Tms.TransporterManagement.Application.Validation;

public sealed class CreatePlacementRequestValidator : AbstractValidator<CreatePlacementRequest>
{
    public CreatePlacementRequestValidator()
    {
        RuleFor(x => x.LoadReference).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TransporterId).GreaterThan(0);
        RuleFor(x => x.RequiredPlacementAt).NotEqual(default(DateTime)).WithMessage("The required placement time is needed.");
        RuleFor(x => x.VehicleTypeReference).GreaterThan(0).When(x => x.VehicleTypeReference.HasValue);
    }
}

public sealed class CreateExecutionRequestValidator : AbstractValidator<CreateExecutionRequest>
{
    public CreateExecutionRequestValidator()
    {
        RuleFor(x => x.LoadReference).NotEmpty().MaximumLength(50);
        RuleFor(x => x.TransporterId).GreaterThan(0);
    }
}

public sealed class RecordExecutionEventRequestValidator : AbstractValidator<RecordExecutionEventRequest>
{
    public RecordExecutionEventRequestValidator()
    {
        RuleFor(x => x.EventType).IsInEnum();
        RuleFor(x => x.EventAt).NotEqual(default(DateTime)).WithMessage("The event time is needed.");
        RuleFor(x => x.EventAt).LessThanOrEqualTo(_ => DateTime.UtcNow.AddHours(1)).WithMessage("An event cannot be recorded in the future.");
        RuleFor(x => x.DelayReasonCode).MaximumLength(40);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public sealed class PodSubmissionRequestValidator : AbstractValidator<PodSubmissionRequest>
{
    public PodSubmissionRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().WithMessage("A POD file is required.")
            .Must(name => DocumentRules.AllowedExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
            .WithMessage("The POD must be a PDF, JPG or PNG file.");
        RuleFor(x => x.ContentType).Must(type => DocumentRules.AllowedContentTypes.Contains(type))
            .WithMessage("The POD must be a PDF, JPG or PNG file.");
        RuleFor(x => x.SizeBytes).GreaterThan(0).LessThanOrEqualTo(DocumentRules.MaxFileSizeBytes)
            .WithMessage("The POD file must be 10 MB or smaller.");
        RuleFor(x => x.PodDate).Must(d => d != default).WithMessage("The POD date is needed.")
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))).WithMessage("The POD date cannot be in the future.");
        RuleFor(x => x.ReceivedBy).NotEmpty().MaximumLength(150);
    }
}
