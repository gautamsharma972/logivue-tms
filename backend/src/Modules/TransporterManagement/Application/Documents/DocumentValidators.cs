using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Workflow;

namespace LogiVue.Tms.TransporterManagement.Application.Validation;

public sealed class SaveDocumentRequestValidator : AbstractValidator<Documents.SaveDocumentRequest>
{
    public SaveDocumentRequestValidator()
    {
        RuleFor(x => x.DocumentTypeId).GreaterThan(0);
        RuleFor(x => x.DocumentNumber).MaximumLength(60);
        RuleFor(x => x.Remarks).MaximumLength(1000);
        RuleFor(x => x.VehicleId).GreaterThan(0).When(x => x.VehicleId.HasValue);
        RuleFor(x => x.DriverId).GreaterThan(0).When(x => x.DriverId.HasValue);
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.IssueDate)
            .When(x => x.IssueDate.HasValue && x.ExpiryDate.HasValue)
            .WithMessage("Expiry date must be on or after the issue date.");
    }
}

public sealed class DocumentUploadValidator : AbstractValidator<Documents.DocumentUpload>
{
    public DocumentUploadValidator()
    {
        RuleFor(x => x.OriginalFileName).NotEmpty().MaximumLength(255)
            .Must(name => Documents.DocumentRules.AllowedExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
            .WithMessage($"Allowed file types: {string.Join(", ", Documents.DocumentRules.AllowedExtensions)}.");
        RuleFor(x => x.ContentType)
            .Must(type => Documents.DocumentRules.AllowedContentTypes.Contains(type))
            .WithMessage("Unsupported content type.");
        RuleFor(x => x.SizeBytes)
            .GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(Documents.DocumentRules.MaxFileSizeBytes)
            .WithMessage("The file must be 10 MB or smaller.");
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required.").MaximumLength(1000);
        RuleFor(x => x.Comments).MaximumLength(1000);
    }
}

public sealed class CommentsRequestValidator : AbstractValidator<CommentsRequest>
{
    public CommentsRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(1000);
    }
}
