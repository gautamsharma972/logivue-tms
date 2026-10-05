using FluentValidation;
using Tms.Modules.Deliveries.Domain;

namespace Tms.Modules.Deliveries.Application;

public sealed record GeoDto(double? Latitude, double? Longitude, double? AccuracyM)
{
    public GeoFix ToFix() => new(Latitude, Longitude, AccuracyM);
}

public sealed record DeliveryItemDto(
    Guid Id, string Sku, string Description, decimal OrderedQuantity, decimal DispatchedQuantity, decimal? DeliveredQuantity, decimal ShortQuantity, decimal DamagedQuantity,
    decimal RejectedQuantity, string UnitOfMeasure, string? Remarks, string? ShortageReasonCode, string? DamageType, string? DamageReason, string? DamageDescription, decimal? Unaccounted);

public sealed record AttemptDto(int AttemptNumber, DateTimeOffset AttemptedAt, AttemptResult Result, string? ReasonCode, string? RecipientName, string? DriverRemarks, string? CustomerRemarks, double? Latitude, double? Longitude);

public sealed record DeliveryEventDto(DateTimeOffset At, DeliveryEventType Type, double? Latitude, double? Longitude, string? DeviceReference, string? Remarks);

public sealed record DiscrepancyDto(Guid Id, Guid ItemId, string Sku, DiscrepancyType Type, decimal Quantity, string? ReasonCode, string? Description, bool CustomerAcknowledged, string? ClaimReference);

public sealed record DeliverySummaryDto(
    Guid Id, string Number, string? ShipmentReference, string CustomerName, string? DestinationReference, Guid? TransporterId, string? TransporterReference, string? VehicleReference,
    DateTimeOffset PlannedDeliveryAt, DateTimeOffset? ActualDeliveryAt, DeliveryStatus Status, DeliveryOutcome? Outcome, PodStatus PodStatus, Guid? PodId, bool HasDiscrepancy, int OpenExceptions);

public sealed record DeliveryDto(
    DeliverySummaryDto Summary,
    string? OrderReference, string? LoadReference, string? TripReference, string? LrNumber, int Sequence, string? DriverName, string? CustomerReference, string? CustomerPhone, string? CustomerEmail,
    string? OriginReference, string? DestinationAddress, double? CustomerLatitude, double? CustomerLongitude, int? GeofenceRadiusM,
    DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, DateTimeOffset? ActualArrivalAt, RemainingDisposition? RemainingDisposition, bool HasQuantityMismatch,
    bool OtpIssued, bool OtpVerified,
    IReadOnlyList<DeliveryItemDto> Items, IReadOnlyList<AttemptDto> Attempts, IReadOnlyList<DeliveryEventDto> Events, IReadOnlyList<DiscrepancyDto> Discrepancies,
    IReadOnlyList<ReconciliationDto> Reconciliation, long Version);

public sealed record ReconciliationDto(Guid ItemId, string Sku, decimal Dispatched, decimal Accounted, decimal Unaccounted, bool Reconciled, IReadOnlyList<string> Problems);

public sealed record CreateDeliveryItemRequest(string Sku, string Description, decimal OrderedQuantity, decimal? DispatchedQuantity, string? UnitOfMeasure);

/// <summary>A delivery received from an external reference (a shipment, a load, a trip) rather than opened by a dispatch.</summary>
public sealed record SaveDeliveryRequest(
    string? ShipmentReference, string? OrderReference, string? LoadReference, string? TripReference, string? LrNumber, int Sequence,
    Guid? TransporterId, string? TransporterReference, Guid? VehicleId, string? VehicleReference, string? DriverName,
    string? CustomerReference, string CustomerName, string? CustomerPhone, string? CustomerEmail,
    string? OriginReference, string? DestinationReference, string? DestinationAddress, double? CustomerLatitude, double? CustomerLongitude, int? GeofenceRadiusM,
    DateTimeOffset PlannedDeliveryAt, DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, IReadOnlyList<CreateDeliveryItemRequest>? Items, long? Version = null);

public sealed record AssignDeliveryRequest(Guid TransporterId, string? TransporterReference, Guid? VehicleId, string? VehicleReference, string? DriverName);

/// <summary>Sent by the device with every action: where it was, which device, and the key that makes a retry harmless.</summary>
public sealed record DeviceContext(GeoDto? Fix, string? DeviceReference, DateTimeOffset? At);

public sealed record AttemptRequest(string ReasonCode, string? DriverRemarks, string? CustomerRemarks, string? RecipientName, DeviceContext? Context);

public sealed record FailDeliveryRequest(string ReasonCode, string? Remarks, DeviceContext? Context);

public sealed record RefuseDeliveryRequest(string ReasonCode, string? RecipientName, string? Remarks, bool CustomerAcknowledged, DeviceContext? Context);

public sealed record ItemQuantityRequest(
    Guid ItemId, decimal DeliveredQuantity, decimal ShortQuantity, decimal DamagedQuantity, decimal RejectedQuantity,
    string? ShortageReasonCode, string? DamageType, string? DamageReason, string? DamageDescription, string? Remarks)
{
    public ItemQuantities ToQuantities() => new(ItemId, DeliveredQuantity, ShortQuantity, DamagedQuantity, RejectedQuantity, ShortageReasonCode, DamageType, DamageReason, DamageDescription, Remarks);
}

public sealed record ProofRequest(ProofMethod Method, string? RecipientName, string? RecipientDesignation, string? RecipientPhone, string? RecipientRemarks, bool DriverConfirmed, bool CustomerAcknowledged);

public sealed record CompleteDeliveryRequest(
    DeliveryOutcome Outcome, IReadOnlyList<ItemQuantityRequest> Items, RemainingDisposition? RemainingDisposition, string? DriverRemarks, ProofRequest Proof, DeviceContext? Context);

public sealed record RescheduleRequest(DateTimeOffset PlannedDeliveryAt, DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd);

public sealed record ReasonRequest(string Reason);

public sealed record VerifyOtpRequest(string Code, DeviceContext? Context);

public sealed record OtpIssuedDto(bool Sent, string? Channel, DateTimeOffset ExpiresAt);

public sealed record ListDeliveriesQuery(
    DeliveryStatus? Status = null, PodStatus? PodStatus = null, string? Search = null, Guid? TransporterId = null, string? Customer = null, DateOnly? From = null, DateOnly? To = null,
    bool? HasException = null, bool? HasDiscrepancy = null, int Page = 1, int PageSize = 25);

internal sealed class SaveDeliveryRequestValidator : AbstractValidator<SaveDeliveryRequest>
{
    public SaveDeliveryRequestValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PlannedDeliveryAt).NotEmpty();
        RuleFor(x => x.ShipmentReference).MaximumLength(40);
        RuleFor(x => x.CustomerEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.GeofenceRadiusM).GreaterThan(0).When(x => x.GeofenceRadiusM.HasValue);
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
            i.RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
            i.RuleFor(x => x.OrderedQuantity).GreaterThanOrEqualTo(0);
        });
    }
}

internal sealed class AssignDeliveryRequestValidator : AbstractValidator<AssignDeliveryRequest>
{
    public AssignDeliveryRequestValidator() => RuleFor(x => x.TransporterId).NotEmpty();
}

internal sealed class AttemptRequestValidator : AbstractValidator<AttemptRequest>
{
    public AttemptRequestValidator() => RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(40);
}

internal sealed class FailDeliveryRequestValidator : AbstractValidator<FailDeliveryRequest>
{
    public FailDeliveryRequestValidator() => RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(40);
}

internal sealed class RefuseDeliveryRequestValidator : AbstractValidator<RefuseDeliveryRequest>
{
    public RefuseDeliveryRequestValidator() => RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(40);
}

internal sealed class CompleteDeliveryRequestValidator : AbstractValidator<CompleteDeliveryRequest>
{
    public CompleteDeliveryRequestValidator()
    {
        RuleFor(x => x.Outcome).IsInEnum();
        RuleFor(x => x.Items).NotNull().NotEmpty();
        RuleFor(x => x.Proof).NotNull();
        RuleFor(x => x.Proof.Method).IsInEnum().When(x => x.Proof is not null);
        RuleFor(x => x.DriverRemarks).MaximumLength(500);
    }
}

internal sealed class RescheduleRequestValidator : AbstractValidator<RescheduleRequest>
{
    public RescheduleRequestValidator() => RuleFor(x => x.PlannedDeliveryAt).NotEmpty();
}

internal sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpRequestValidator() => RuleFor(x => x.Code).NotEmpty().Length(4, 8).Matches("^[0-9]+$").WithMessage("Enter the numeric code.");
}
