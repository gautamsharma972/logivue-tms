using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Contracts;

// ---- What Tracking needs from planning, and what it gives back. Tracking never reads another module's tables: Shipments implements the planning side, and the events below are
// ---- published through the transactional outbox for Deliveries, Transporters and (later) Claims to subscribe to.

public sealed record TrackingPoint(double Latitude, double Longitude);

/// <param name="Kind">"Pickup" or "Drop".</param>
/// <param name="ExpectedDwellMinutes">How long the vehicle is expected to stay; null means the tenant's default for the kind of stop applies.</param>
public sealed record TrackingStopFact(
    Guid? OrderId, int Sequence, string Kind, string Name, string? City, double? Latitude, double? Longitude, DateTimeOffset? PlannedArrival,
    DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, int? ExpectedDwellMinutes, string? Reference, string? CustomerReference, string? CustomerName);

/// <summary>Everything planning knows about a trip that tracking needs: who carries it, where it goes, when it should get there and the route to follow.</summary>
/// <param name="RouteSource">"Osrm" or "Estimate": an estimate is a straight line between stops, never presented as a road route.</param>
public sealed record PlannedTrackingContext(
    Guid ShipmentId, string TripReference, string ShipmentReference, Guid TransporterId, string? TransporterReference, string? VehicleReference, string? DriverName, string? DriverPhone,
    DateTimeOffset? PlannedStart, decimal? PlannedDistanceKm, int? PlannedDurationMinutes, string RouteSource, IReadOnlyList<TrackingPoint> Route, IReadOnlyList<TrackingStopFact> Stops);

/// <summary>Implemented by Shipments (the planning side). Tracking has a local stand-in so it runs without it.</summary>
public interface ITrackingPlanningIntegration
{
    Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(string tripReference, CancellationToken cancellationToken);

    Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(Guid shipmentId, CancellationToken cancellationToken);
}

/// <summary>Tracking quality and carrier behaviour seen on the road. Tracking quality is deliberately a separate measure from performance.</summary>
/// <param name="Kind">PickupDelay, DeliveryDelay, RouteDeviation, ExcessDwell, UnplannedStop, TrackingCompliance.</param>
public sealed record TrackingPerformanceEvent(
    Guid TenantId, Guid? TransporterId, string ShipmentReference, string TripReference, string Kind, decimal? Value, string? Detail, DateTimeOffset At) : DomainEvent;

/// <summary>A vehicle did something at a customer's site that Delivery needs to know: entered the geofence, arrived, left.</summary>
/// <param name="Kind">EnteredSite, ArrivedAtSite, DepartedSite.</param>
public sealed record DeliveryTrackingEvent(
    Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, Guid? OrderId, string? StopReference, string Kind, DateTimeOffset At, double? Latitude, double? Longitude) : DomainEvent;

public interface ITrackingTransporterIntegration
{
    Task PublishTrackingPerformanceEventAsync(TrackingPerformanceEvent eventData, CancellationToken cancellationToken);
}

public interface ITrackingDeliveryIntegration
{
    Task PublishDeliveryMilestoneAsync(DeliveryTrackingEvent eventData, CancellationToken cancellationToken);
}

/// <summary>What a claim can use from the journey: where the vehicle went, what it deviated from, where it stopped and when it arrived.</summary>
public sealed record TrackingEvidence(
    string TripReference, string ShipmentReference, string VehicleReference, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, decimal PlannedKm, decimal ActualKm,
    int LocationPoints, IReadOnlyList<string> Deviations, IReadOnlyList<string> Stops, IReadOnlyList<string> Arrivals, IReadOnlyList<TrackingPoint> Path);

/// <summary>Implemented by Tracking; a future Claims module reads it.</summary>
public interface ITrackingClaimsIntegration
{
    Task<TrackingEvidence?> GetEvidenceAsync(string tripReference, CancellationToken cancellationToken);
}

// ---- Events through the outbox. Each says what happened and to which trip; none carries the raw GPS trail.

public abstract record TrackingEvent(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At) : DomainEvent;

public sealed record TrackingStarted(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record TrackingStopped(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, string Reason)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record TrackingCompleted(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, decimal ActualKm)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record TrackingStale(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, int MinutesSinceLastLocation)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record TrackingLost(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, int MinutesSinceLastLocation)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record TrackingVehicleArrived(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, string StopName, string StopKind)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record TrackingVehicleDeparted(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, string StopName, string StopKind)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record EnteredGeofence(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, string GeofenceCode, string GeofenceType)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record ExitedGeofence(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, string GeofenceCode, string GeofenceType)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record RouteDeviationDetected(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, decimal DistanceFromRouteKm)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record RouteDeviationResolved(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, int DurationMinutes)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record ExcessiveDwellDetected(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, string Where, int ExcessMinutes)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record UnplannedStopDetected(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, int Minutes)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

/// <summary>The estimated arrival changed enough to matter (not on every location).</summary>
public sealed record EtaUpdated(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, DateTimeOffset Eta, int DelayMinutes)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record ShipmentAtRisk(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, DateTimeOffset Eta, int DelayMinutes)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);

public sealed record ShipmentDelayed(Guid TenantId, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId, DateTimeOffset At, DateTimeOffset Eta, int DelayMinutes)
    : TrackingEvent(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, At);
