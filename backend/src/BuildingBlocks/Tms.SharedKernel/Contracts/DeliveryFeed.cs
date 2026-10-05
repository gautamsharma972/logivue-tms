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
public sealed record DeliveryCompleted(Guid DeliveryId, Guid TenantId, string Number, Guid? ShipmentId, Guid? TransporterId, DateTimeOffset DeliveredAt, bool HasDiscrepancy) : DomainEvent;

/// <summary>A proof of delivery was accepted. Freight audit, claims and transporter performance react to this.</summary>
public sealed record PodAccepted(
    Guid DeliveryId, Guid PodId, Guid TenantId, string DeliveryNumber, Guid? ShipmentId, Guid? TransporterId, DateTimeOffset DeliveredAt, DateTimeOffset? SubmittedAt, DateTimeOffset AcceptedAt,
    bool AcceptedFirstTime, decimal ShortQuantity, decimal DamagedQuantity) : DomainEvent;

public sealed record PodRejected(Guid DeliveryId, Guid PodId, Guid TenantId, string DeliveryNumber, Guid? TransporterId, string Reason, DateTimeOffset RejectedAt) : DomainEvent;

public sealed record DeliveryExceptionRaised(Guid ExceptionId, Guid DeliveryId, Guid TenantId, string DeliveryNumber, Guid? TransporterId, string ExceptionType, string Severity) : DomainEvent;
