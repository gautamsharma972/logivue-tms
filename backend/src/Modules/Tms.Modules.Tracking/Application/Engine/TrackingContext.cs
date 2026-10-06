using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Domain;

namespace Tms.Modules.Tracking.Application.Engine;

/// <summary>Something to tell the people watching, held until the change that caused it has been saved.</summary>
public sealed record TrackingNotification(Guid TenantId, string Kind, string Title, string Message, Severity Severity, Guid? ShipmentId, string? TripReference, Guid? TransporterId = null);

/// <summary>
/// Everything the tracking engine needs to know about one trip while it works through a batch of locations: the trip, its stops and route, the memory of where it stands against each
/// geofence, the alerts already raised. Loaded once and updated in memory; the pipeline saves once at the end.
/// </summary>
internal sealed class TrackingContext
{
    public required Guid TenantId { get; init; }

    public required TrackedShipment Shipment { get; init; }

    public required TrackingSession Session { get; init; }

    public required SettingsSnapshot Settings { get; init; }

    public required DateTimeOffset Now { get; init; }

    public RouteGeometry? Route { get; init; }

    public required IReadOnlyList<ShipmentStop> Stops { get; init; }

    public required IReadOnlyList<Geofence> SharedGeofences { get; init; }

    public required Dictionary<Guid, GeofencePresence> Presences { get; init; }

    public required List<Milestone> Milestones { get; init; }

    public required List<TrackingAlert> Alerts { get; init; }

    public required List<TrackingException> Exceptions { get; init; }

    public RouteDeviation? OpenDeviation { get; set; }

    public DwellEvent? OpenDwell { get; set; }

    public TrackingGap? OpenGap { get; set; }

    public List<IDomainEvent> Events { get; } = [];

    public List<TrackingNotification> Notifications { get; } = [];

    public bool HasUsableRoute => Route is { IsUsable: true };

    // ---- the fix being worked on

    public LocationInput Fix { get; set; } = null!;

    public GeoPoint Point => new(Fix.Latitude, Fix.Longitude);

    public DateTimeOffset ReceivedAt { get; set; }

    public double? AlongKm { get; set; }

    public double? OffRouteKm { get; set; }
}
