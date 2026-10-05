using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Contracts;

/// <summary>One drop of a dispatched shipment: what is to be delivered to whom, where.</summary>
public sealed record DeliveryDropFact(
    Guid OrderId,
    string OrderNumber,
    string? OrderReference,
    int Sequence,
    string? LrNumber,
    string CustomerName,
    string? CustomerPhone,
    string AddressLine,
    string City,
    string State,
    string Pincode,
    double? Latitude,
    double? Longitude,
    string Description,
    int? Packages,
    decimal WeightKg,
    DateOnly? DeliverBy,
    TimeOnly? WindowFrom,
    TimeOnly? WindowTo);

/// <summary>A dispatched shipment as the Deliveries module needs it: the vehicle, driver and the drops, in order. Return pickups are not deliveries.</summary>
public sealed record DeliveryPlanFact(
    Guid ShipmentId,
    string ShipmentNumber,
    Guid TransporterId,
    Guid? VehicleId,
    string? VehicleRegistration,
    string? DriverName,
    string OriginCity,
    DateOnly PlannedPickupDate,
    IReadOnlyList<DeliveryDropFact> Drops);

/// <summary>Read-only view of a dispatched shipment, owned by Shipments and consumed by Deliveries so nothing is copied that could drift.</summary>
public interface IShipmentDeliveryFeed
{
    Task<DeliveryPlanFact?> GetAsync(Guid shipmentId, CancellationToken cancellationToken = default);
}

/// <summary>A delivery was confirmed by the driver (fully or partly). The proof of delivery follows separately.</summary>
public sealed record DeliveryCompleted(Guid DeliveryId, Guid TenantId, string Number, Guid? ShipmentId, Guid? TransporterId, DateTimeOffset DeliveredAt, bool HasDiscrepancy, bool? OnTime = null, decimal ShortQuantity = 0, decimal DamagedQuantity = 0) : DomainEvent;

/// <summary>A proof was sent for checking. Freight audit keeps the invoice on hold until it is accepted.</summary>
public sealed record PodSubmitted(Guid DeliveryId, Guid PodId, Guid TenantId, string DeliveryNumber, Guid? ShipmentId, Guid? TransporterId, DateTimeOffset SubmittedAt) : DomainEvent;

/// <summary>A proof of delivery was accepted. Freight audit, claims and transporter performance react to this.</summary>
public sealed record PodAccepted(
    Guid DeliveryId, Guid PodId, Guid TenantId, string DeliveryNumber, Guid? ShipmentId, Guid? TransporterId, DateTimeOffset DeliveredAt, DateTimeOffset? SubmittedAt, DateTimeOffset AcceptedAt,
    bool AcceptedFirstTime, decimal ShortQuantity, decimal DamagedQuantity, bool? SubmittedWithinSla = null, bool? DeliveredOnTime = null) : DomainEvent;

public sealed record PodRejected(Guid DeliveryId, Guid PodId, Guid TenantId, string DeliveryNumber, Guid? TransporterId, string Reason, DateTimeOffset RejectedAt) : DomainEvent;

public sealed record DeliveryExceptionRaised(Guid ExceptionId, Guid DeliveryId, Guid TenantId, string DeliveryNumber, Guid? TransporterId, string ExceptionType, string Severity) : DomainEvent;

// ---- Hand-offs to claims, freight audit and planning. Each is a contract another module can implement; Deliveries ships a local adapter for each so it works alone.

/// <summary>Everything a claim needs, so nobody retypes it: what was found, where, by whom, on which load, and the evidence.</summary>
public sealed record ClaimRequest(
    Guid DeliveryId, string DeliveryNumber, Guid? ShipmentId, string? ShipmentReference, string CustomerName, string? CustomerReference, Guid? TransporterId, string? TransporterReference,
    string? VehicleReference, string Sku, string ItemDescription, string DiscrepancyType, decimal Quantity, string? ReasonCode, string? Description, Guid? PodId, string? PodNumber,
    IReadOnlyList<Guid> EvidenceIds, bool CustomerAcknowledged, string? RecipientName, double? Latitude, double? Longitude, DateTimeOffset OccurredAt, string? Remarks);

public sealed record ClaimReference(string Reference, string System);

public interface IClaimsIntegration
{
    Task<ClaimReference> CreateClaimAsync(ClaimRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Status">Pending, Submitted, Accepted, Rejected or ResubmissionRequired.</param>
/// <param name="BillingEligible">The freight for this load may now be audited and paid.</param>
/// <param name="InvoiceHold">The invoice must wait (the proof is missing, being checked or was rejected).</param>
public sealed record PodStatusChange(
    Guid DeliveryId, Guid? PodId, string DeliveryNumber, Guid? ShipmentId, string? ShipmentReference, Guid? TransporterId, string Status, bool BillingEligible, bool InvoiceHold, DateTimeOffset At);

public interface IFreightAuditIntegration
{
    Task NotifyPodStatusChangedAsync(PodStatusChange change, CancellationToken cancellationToken = default);
}

/// <param name="OnTimeRate">Share of deliveries within their window; null when none could be judged.</param>
public sealed record LaneReliability(string Origin, string Destination, int Days, int Deliveries, decimal? OnTimeRate, decimal? FailureRate, decimal? RefusalRate, decimal? AverageProofHours);

/// <summary>How dependably deliveries on a lane have gone, for planning to weigh. Read-only; Planning does not have to use it.</summary>
public interface IDeliveryReliabilityFeed
{
    Task<LaneReliability> GetLaneReliabilityAsync(string originCity, string destinationCity, int days, CancellationToken cancellationToken = default);
}
