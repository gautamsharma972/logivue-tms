using FluentValidation;
using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Application.Operations;

public sealed record PlacementEventDto(string EventType, DateTimeOffset EventAt, string? Remarks);

/// <param name="SlaStatus">OnTime, Late, Pending, Overdue, NoShow or Cancelled, against the placement grace period.</param>
/// <param name="DelayMinutes">Minutes after the required placement time. Null until the vehicle is placed.</param>
public sealed record PlacementDto(
    Guid Id, Guid ShipmentId, string ShipmentNumber, Guid TransporterId, Guid? VehicleId, string? VehicleRegistration, DateTimeOffset RequiredAt, DateTimeOffset? ReportedAt,
    DateTimeOffset? PlacedAt, DateTimeOffset? LoadingStartedAt, PlacementStatus Status, string SlaStatus, int? DelayMinutes, int ReplacementCount, string? Reason,
    IReadOnlyList<PlacementEventDto> Events);

public sealed record ListPlacementsQuery(Guid? TransporterId = null, PlacementStatus? Status = null, int Page = 1, int PageSize = 25);

public sealed record ReasonRequest(string Reason);

public sealed record ClaimDto(
    Guid Id, Guid TransporterId, Guid? ShipmentId, string? ShipmentNumber, ClaimType ClaimType, DateOnly ClaimDate, decimal ClaimValue, ClaimStatus Status, string? Remarks, DateTimeOffset? ResolvedAt);

public sealed record RecordClaimRequest(ClaimType ClaimType, DateOnly ClaimDate, decimal ClaimValue, Guid? ShipmentId, string? Remarks);

public sealed record SetClaimValueRequest(decimal ClaimValue);

public sealed record LoadCostDto(Guid Id, Guid TransporterId, Guid ShipmentId, string ShipmentNumber, DateOnly ServiceDate, decimal AgreedAmount, decimal InvoicedAmount, bool OnBudget);

/// <param name="AgreedAmount">What was agreed. Defaults to the freight estimate the shipment was accepted at.</param>
public sealed record RecordLoadCostRequest(Guid ShipmentId, decimal InvoicedAmount, decimal? AgreedAmount = null);

public sealed record CapacityDayDto(Guid Id, Guid TransporterId, DateOnly Date, int VehiclesCommitted, int VehiclesAvailable);

public sealed record SaveCapacityRequest(DateOnly Date, int VehiclesCommitted, int VehiclesAvailable);

public sealed record AlertDto(
    Guid Id, string AlertType, AlertSeverity Severity, Guid TransporterId, string? TransporterName, Guid? ShipmentId, string? ShipmentNumber, string Message, AlertStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt, string? Resolution);

public sealed record ListAlertsQuery(AlertStatus? Status = null, AlertSeverity? Severity = null, Guid? TransporterId = null, int Page = 1, int PageSize = 25);

public sealed record ResolveAlertRequest(string? Comments);

internal sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class RecordLoadCostRequestValidator : AbstractValidator<RecordLoadCostRequest>
{
    public RecordLoadCostRequestValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.InvoicedAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AgreedAmount).GreaterThan(0).When(x => x.AgreedAmount.HasValue);
    }
}

internal sealed class SaveCapacityRequestValidator : AbstractValidator<SaveCapacityRequest>
{
    public SaveCapacityRequestValidator()
    {
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.VehiclesCommitted).InclusiveBetween(0, 10_000);
        RuleFor(x => x.VehiclesAvailable).InclusiveBetween(0, 10_000).LessThanOrEqualTo(x => x.VehiclesCommitted).WithMessage("Available vehicles cannot exceed committed vehicles.");
    }
}
