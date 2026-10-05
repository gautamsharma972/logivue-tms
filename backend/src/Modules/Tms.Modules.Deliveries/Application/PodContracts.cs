using FluentValidation;
using Microsoft.AspNetCore.Http;
using Tms.Modules.Deliveries.Domain;

namespace Tms.Modules.Deliveries.Application;

public sealed record EvidenceDto(
    Guid Id, EvidenceType Type, string FileName, string ContentType, long SizeBytes, string FileHash, DateTimeOffset CapturedAt, double? Latitude, double? Longitude, string? DeviceReference,
    int? Width, int? Height, string? Warnings, bool Removed, string? RemovedReason);

public sealed record SignatureDto(Guid Id, string SignerName, string? SignerDesignation, DateTimeOffset CapturedAt, double? Latitude, double? Longitude, string VerificationMethod);

public sealed record PodItemDto(Guid DeliveryItemId, string Sku, decimal OrderedQuantity, decimal DispatchedQuantity, decimal DeliveredQuantity, decimal ShortQuantity, decimal DamagedQuantity, decimal RejectedQuantity, string? Remarks);

public sealed record ValidationDto(string Type, string Check, ValidationOutcome Status, string Message, DateTimeOffset ValidatedAt);

public sealed record ReviewActionDto(DateTimeOffset At, string Action, string? FieldName, string? OldValue, string? NewValue, string? Reason, Guid? By);

public sealed record OcrFieldDto(
    string Name, string? RawValue, string? NormalizedValue, decimal Confidence, OcrFieldStatus Status, string? Message, string? ReviewedValue, string? EffectiveValue, string? Expected, decimal Threshold);

public sealed record OcrResultDto(Guid Id, Guid EvidenceId, string Provider, OcrStatus Status, decimal? OverallConfidence, DateTimeOffset QueuedAt, DateTimeOffset? ProcessedAt, string? Error, IReadOnlyList<OcrFieldDto> Fields);

public sealed record PodSummaryDto(
    Guid Id, string PodNumber, int Version, bool IsCurrent, Guid DeliveryId, string DeliveryNumber, string CustomerName, string? TransporterReference, PodStatus Status,
    DateTimeOffset? DeliveredAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt, decimal? HoursSinceSubmitted, ValidationOutcome Validation, bool HasDiscrepancy, OcrStatus? Ocr);

public sealed record PodDto(
    PodSummaryDto Summary, ProofMethod? Method, string? RecipientName, string? RecipientDesignation, string? RecipientPhone, DateTimeOffset? ArrivalAt, DateTimeOffset CapturedAt,
    DateTimeOffset? ReviewedAt, double? Latitude, double? Longitude, double? GpsAccuracy, GeofenceStatus Geofence, string? DriverRemarks, string? RecipientRemarks, bool DriverConfirmed,
    bool OtpVerified, bool CustomerAcknowledged, string? RejectionReason, int RejectionCount, bool AutoAccepted,
    IReadOnlyList<PodItemDto> Items, IReadOnlyList<EvidenceDto> Evidence, IReadOnlyList<SignatureDto> Signatures, IReadOnlyList<ValidationDto> Validations,
    IReadOnlyList<ReviewActionDto> Reviews, IReadOnlyList<OcrResultDto> Ocr, IReadOnlyList<string> Missing, long RowVersion);

/// <summary>Everything the review workbench shows at once: the original document, what was read from it, and how that compares with the system.</summary>
public sealed record PodReviewDto(PodDto Pod, DeliveryDto Delivery, Guid? DocumentEvidenceId, OcrResultDto? Ocr);

public sealed class UploadEvidenceForm
{
    public IFormFile? File { get; init; }

    public EvidenceType Type { get; init; } = EvidenceType.PackagePhoto;

    public DateTimeOffset? CapturedAt { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public double? AccuracyM { get; init; }

    public string? DeviceReference { get; init; }

    /// <summary>The device's own key for this capture; uploading it again returns the first upload.</summary>
    public string? ClientRecordId { get; init; }
}

public sealed class UploadSignatureForm
{
    public IFormFile? File { get; init; }

    public string? SignerName { get; init; }

    public string? SignerDesignation { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? ClientRecordId { get; init; }
}

public sealed record UpdateProofRequest(ProofRequest Proof);

public sealed record ReviewPodRequest(string Action, string? Reason);

public sealed record ReviewOcrFieldRequest(string Field, string Value, string Reason);

public sealed record RequestCorrectionRequest(string Reason);

public sealed record ListPodsQuery(PodStatus? Status = null, string? Search = null, Guid? TransporterId = null, bool? Overdue = null, bool CurrentOnly = true, int Page = 1, int PageSize = 25);

internal sealed class ReviewPodRequestValidator : AbstractValidator<ReviewPodRequest>
{
    public ReviewPodRequestValidator()
    {
        RuleFor(x => x.Action).NotEmpty().Must(a => ReviewActions.All.Contains(a, StringComparer.OrdinalIgnoreCase)).WithMessage("Choose accept, reject or resubmission.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

internal sealed class ReviewOcrFieldRequestValidator : AbstractValidator<ReviewOcrFieldRequest>
{
    public ReviewOcrFieldRequestValidator()
    {
        RuleFor(x => x.Field).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Value).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithMessage("Say why the value is being changed: every edit is audited.");
    }
}

internal sealed class RequestCorrectionRequestValidator : AbstractValidator<RequestCorrectionRequest>
{
    public RequestCorrectionRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class UpdateProofRequestValidator : AbstractValidator<UpdateProofRequest>
{
    public UpdateProofRequestValidator() => RuleFor(x => x.Proof).NotNull();
}

public static class ReviewActions
{
    public const string Accept = "accept";
    public const string Reject = "reject";
    public const string Resubmission = "resubmission";

    public static IReadOnlyList<string> All { get; } = [Accept, Reject, Resubmission];
}
