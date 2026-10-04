using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Contracts;

public enum FreightMode
{
    /// <summary>Full truck load.</summary>
    Ftl = 1,

    /// <summary>Part truck load.</summary>
    Ptl = 2,
}

public sealed record FreightQuoteRequest(
    DateOnly Date,
    string OriginState,
    string? OriginCity,
    string DestinationState,
    string? DestinationCity,
    Guid? VehicleTypeId,
    FreightMode? Mode,
    decimal? WeightKg,
    decimal? VolumeCbm,
    decimal? DistanceKm,
    int Drops = 1);

public sealed record FreightQuoteLine(string Code, string Description, decimal Amount);

public sealed record FreightQuoteResult(
    Guid ContractId,
    string ContractReference,
    Guid TransporterId,
    string TransporterName,
    FreightMode Mode,
    string Lane,
    decimal? ChargeableWeightKg,
    IReadOnlyList<FreightQuoteLine> Lines,
    IReadOnlyList<string> Notes,
    decimal Total);

/// <param name="Message">Why there are no (or fewer) quotes, e.g. no rate covers the lane or a weight is missing.</param>
public sealed record FreightQuoteSet(IReadOnlyList<FreightQuoteResult> Quotes, string? Message);

/// <summary>
/// The freight price engine as other modules see it (implemented by Contracts). The caller is responsible for its own
/// authorisation; tenant isolation still applies.
/// </summary>
public interface IFreightQuoteService
{
    Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default);
}

public enum FleetCompliance
{
    Compliant = 1,
    ExpiringSoon = 2,
    NonCompliant = 3,
}

public enum FleetAvailability
{
    Available = 1,
    InMaintenance = 2,
    OffRoad = 3,
}

public sealed record FleetVehicle(
    Guid Id,
    Guid TransporterId,
    string RegistrationNumber,
    Guid VehicleTypeId,
    string VehicleTypeName,
    int PayloadKg,
    bool IsActive,
    FleetCompliance Compliance,
    IReadOnlyList<string> Issues,
    FleetAvailability Availability = FleetAvailability.Available,
    DateOnly? AvailableFrom = null,
    DateOnly? AvailableTo = null,
    string? AvailabilityNote = null)
{
    /// <summary>
    /// A vehicle that is Available is usable inside its optional window. One in maintenance or off the road is usable again
    /// only from its <see cref="AvailableFrom"/> date (when it returns to service), and until <see cref="AvailableTo"/> if set.
    /// </summary>
    public bool IsAvailableOn(DateOnly date) =>
        IsActive
        && (Availability == FleetAvailability.Available ? AvailableFrom is null || date >= AvailableFrom : AvailableFrom is { } back && date >= back)
        && (AvailableTo is null || date <= AvailableTo);

    public string WhyUnavailableOn(DateOnly date) =>
        !IsActive ? "inactive"
        : Availability != FleetAvailability.Available && (AvailableFrom is null || date < AvailableFrom)
            ? $"{(Availability == FleetAvailability.InMaintenance ? "in maintenance" : "off the road")}{(AvailableFrom is { } d ? $" until {d:dd MMM}" : string.Empty)}{(string.IsNullOrWhiteSpace(AvailabilityNote) ? string.Empty : $" ({AvailabilityNote})")}"
        : AvailableFrom is { } from && date < from ? $"not available before {from:dd MMM}"
        : AvailableTo is { } to && date > to ? $"not available after {to:dd MMM}"
        : "available";
}

public sealed record FleetDriver(
    Guid Id,
    Guid TransporterId,
    string FullName,
    string Phone,
    string? LicenseNumber,
    bool IsActive,
    FleetCompliance Compliance,
    IReadOnlyList<string> Issues);

/// <summary>A transporter's vehicles and drivers with their paperwork status, for allocation (implemented by Transporters).</summary>
public interface IFleetDirectory
{
    Task<FleetVehicle?> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    Task<FleetDriver?> GetDriverAsync(Guid driverId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FleetVehicle>> ListVehiclesAsync(Guid transporterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FleetDriver>> ListDriversAsync(Guid transporterId, CancellationToken cancellationToken = default);
}

/// <summary>Per-tenant gap-tolerant running numbers (ORD-00001, SH-00001…) for modules that do not own a sequence table.</summary>
public interface ISequenceGenerator
{
    Task<long> NextAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);
}

/// <summary>A shipment has left. Tracking, billing and customer notifications react to this.</summary>
public sealed record ShipmentDispatched(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, DateTimeOffset DispatchedAt) : DomainEvent;

/// <summary>A shipment reached its destination (proof of delivery and billing build on this).</summary>
public sealed record ShipmentDelivered(Guid ShipmentId, Guid TenantId, string Number, Guid TransporterId, DateTimeOffset DeliveredAt) : DomainEvent;

/// <summary>A consignee's proof of delivery was checked and accepted. Freight billing releases payment on this.</summary>
public sealed record PodVerified(
    Guid ShipmentId, Guid OrderId, Guid TenantId, string ShipmentNumber, string OrderNumber, Guid TransporterId, DateTimeOffset VerifiedAt) : DomainEvent;

/// <summary>Goods arrived short or damaged. Claims start from this.</summary>
public sealed record DeliveryExceptionReported(
    Guid ShipmentId, Guid OrderId, Guid TenantId, string ShipmentNumber, string OrderNumber, Guid TransporterId,
    int ShortagePackages, int DamagedPackages, string? Remarks, DateTimeOffset ReportedAt) : DomainEvent;
