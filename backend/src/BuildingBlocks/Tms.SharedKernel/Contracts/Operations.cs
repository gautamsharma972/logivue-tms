using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Contracts;

/// <summary>A load was offered to a transporter. Transporter performance counts tender acceptance from this and the two events below.</summary>
public sealed record ShipmentTendered(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, DateTimeOffset TenderedAt) : DomainEvent;

public sealed record ShipmentAccepted(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, DateTimeOffset AcceptedAt) : DomainEvent;

public sealed record ShipmentRejected(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, DateTimeOffset RejectedAt, string Reason) : DomainEvent;

/// <summary>A shipment that had been offered or accepted was cancelled. Open placements and loads for it are closed.</summary>
public sealed record ShipmentCancelled(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, string Reason) : DomainEvent;

/// <summary>The vehicle or driver on an accepted shipment was swapped before it left. Counted as a vehicle replacement.</summary>
public sealed record ShipmentVehicleReassigned(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, Guid VehicleId, string Registration) : DomainEvent;

/// <summary>One delivered order of a shipment, as far as proof of delivery is concerned.</summary>
/// <param name="FirstProofAt">When the first proof document was uploaded; null if none yet.</param>
/// <param name="ProofStatus">Awaiting, Uploaded, Verified or Rejected.</param>
/// <param name="ProofRejections">How many times a proof was refused and had to be uploaded again.</param>
public sealed record OrderDeliveryFact(
    Guid OrderId, DateTimeOffset DeliveredAt, DateOnly? DeliverBy, DateTimeOffset? FirstProofAt, string ProofStatus, int ProofRejections, bool HadException);

/// <summary>
/// A shipment that was accepted by a transporter, with the times and outcomes performance is measured from. Read-only: owned by
/// Shipments, consumed by Transporters.
/// </summary>
public sealed record ShipmentFact(
    Guid ShipmentId,
    string Number,
    Guid TransporterId,
    FreightMode Mode,
    Guid? VehicleTypeId,
    string OriginState,
    string OriginCity,
    string? DestinationState,
    string? DestinationCity,
    DateOnly PlannedPickupDate,
    DateOnly? DeliverBy,
    decimal? FreightEstimate,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    IReadOnlyList<OrderDeliveryFact> Deliveries,
    Guid? VehicleId = null,
    string? VehicleRegistration = null);

public interface IShipmentOperationsFeed
{
    /// <summary>Shipments of one transporter that were accepted, dispatched or delivered in the range (by the date of the relevant event).</summary>
    Task<IReadOnlyList<ShipmentFact>> ListAsync(Guid transporterId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>One shipment's facts, or null if it is unknown or has no transporter (it was never accepted, or was declined).</summary>
    Task<ShipmentFact?> GetAsync(Guid shipmentId, CancellationToken cancellationToken = default);

    /// <summary>The same for every transporter, for ranking and benchmarks.</summary>
    Task<IReadOnlyList<ShipmentFact>> ListAllAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
