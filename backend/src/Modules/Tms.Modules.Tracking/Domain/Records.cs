using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Domain;

/// <summary>A tenant's own value for one setting. Absent means the default in <see cref="TrackingSettingDefaults"/> applies.</summary>
public sealed class TrackingSetting : AggregateRoot, ITenantScoped
{
    private TrackingSetting()
    {
    }

    public Guid TenantId { get; private set; }

    public string Key { get; private set; } = null!;

    public string ValueJson { get; private set; } = "{}";

    public static TrackingSetting Create(Guid tenantId, string key, string json) => new() { TenantId = tenantId, Key = key, ValueJson = json };

    public void Change(string json) => ValueJson = json;
}

/// <summary>
/// One tracking session: a vehicle, a driver and a device following one trip from start to stop. Tracking exists only for the length of the trip; the session is how a location is
/// tied to the trip it belongs to.
/// </summary>
public sealed class TrackingSession : AggregateRoot, ITenantScoped
{
    private TrackingSession()
    {
    }

    public Guid TenantId { get; private set; }

    public string Reference { get; private set; } = null!;

    public Guid TrackedShipmentId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public string? DriverReference { get; private set; }

    public Guid? TransporterId { get; private set; }

    public string? DeviceId { get; private set; }

    public TrackingSessionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? StoppedAt { get; private set; }

    public string? StopReason { get; private set; }

    public Guid? StartedBy { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastLocationAt { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastReceivedAt { get; private set; }

    [AuditIgnore]
    public int LocationCount { get; private set; }

    public bool IsOpen => Status is TrackingSessionStatus.Active or TrackingSessionStatus.Paused or TrackingSessionStatus.Stale or TrackingSessionStatus.Lost;

    public static TrackingSession Start(
        Guid tenantId, string reference, TrackedShipment shipment, string? driverReference, string? vehicleReference, string? deviceId, Guid? startedBy, DateTimeOffset at) => new()
    {
        TenantId = tenantId, Reference = reference, TrackedShipmentId = shipment.Id, ShipmentId = shipment.ShipmentId, TripReference = shipment.TripReference, ShipmentReference = shipment.ShipmentReference,
        VehicleReference = string.IsNullOrWhiteSpace(vehicleReference) ? shipment.VehicleReference : vehicleReference.Trim(), DriverReference = driverReference, TransporterId = shipment.TransporterId,
        DeviceId = deviceId, Status = TrackingSessionStatus.Active, StartedAt = at, StartedBy = startedBy,
    };

    public void Located(DateTimeOffset capturedAt, DateTimeOffset receivedAt, int count)
    {
        if (LastLocationAt is null || capturedAt > LastLocationAt)
        {
            LastLocationAt = capturedAt;
        }

        LastReceivedAt = receivedAt;
        LocationCount += count;
        if (Status is TrackingSessionStatus.Stale or TrackingSessionStatus.Lost)
        {
            Status = TrackingSessionStatus.Active; // it is reporting again
        }
    }

    public void MarkStale() { if (Status == TrackingSessionStatus.Active) { Status = TrackingSessionStatus.Stale; } }

    public void MarkLost() { if (Status is TrackingSessionStatus.Active or TrackingSessionStatus.Stale) { Status = TrackingSessionStatus.Lost; } }

    public Result Pause()
    {
        if (Status is not (TrackingSessionStatus.Active or TrackingSessionStatus.Stale or TrackingSessionStatus.Lost))
        {
            return Error.Conflict("tracking.not_active", "Only a session that is running can be paused.");
        }

        Status = TrackingSessionStatus.Paused;
        return Result.Success();
    }

    public Result Resume()
    {
        if (Status != TrackingSessionStatus.Paused)
        {
            return Error.Conflict("tracking.not_paused", "Only a paused session can be resumed.");
        }

        Status = TrackingSessionStatus.Active;
        return Result.Success();
    }

    public void ChangeDevice(string deviceId) => DeviceId = deviceId;

    public Result Stop(DateTimeOffset at, string reason, bool completed)
    {
        if (!IsOpen)
        {
            return Error.Conflict("tracking.already_stopped", "Tracking for this trip has already stopped.");
        }

        Status = completed ? TrackingSessionStatus.Completed : TrackingSessionStatus.Cancelled;
        StoppedAt = at;
        StopReason = reason;
        return Result.Success();
    }
}

/// <summary>One GPS fix, kept exactly as it was captured. Captured time (when the device read the position) and received time (when the server heard of it) are never confused.</summary>
[AuditIgnore]
public sealed class TrackingLocation : Entity, ITenantScoped
{
    private TrackingLocation()
    {
    }

    public Guid TenantId { get; private set; }

    public string ClientLocationReference { get; private set; } = null!;

    public Guid SessionId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public string? DriverReference { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public double? AccuracyMeters { get; private set; }

    public double? SpeedKph { get; private set; }

    public double? Heading { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public string DeviceId { get; private set; } = null!;

    public TrackingSource Source { get; private set; }

    public LocationValidation Validation { get; private set; }

    public LocationAnomaly Anomalies { get; private set; }

    public string? Reasons { get; private set; }

    /// <summary>Arrived after the trip had already moved on past it (offline sync): kept as history, never allowed to rewind the vehicle.</summary>
    public bool IsLate { get; private set; }

    public string? AppVersion { get; private set; }

    public int? BatteryPercentage { get; private set; }

    public string? NetworkType { get; private set; }

    public static TrackingLocation Create(
        Guid tenantId, TrackingSession session, string clientReference, LocationInput fix, DateTimeOffset receivedAt, string deviceId, TrackingSource source, LocationVerdict verdict, bool isLate,
        string? appVersion, int? battery, string? network) => new()
    {
        TenantId = tenantId, ClientLocationReference = clientReference, SessionId = session.Id, ShipmentId = session.ShipmentId, TripReference = session.TripReference, ShipmentReference = session.ShipmentReference,
        VehicleReference = session.VehicleReference, DriverReference = session.DriverReference, Latitude = fix.Latitude, Longitude = fix.Longitude, AccuracyMeters = fix.AccuracyMeters,
        SpeedKph = fix.SpeedKph, Heading = fix.Heading, CapturedAt = fix.CapturedAt, ReceivedAt = receivedAt, DeviceId = deviceId, Source = source, Validation = verdict.Status, Anomalies = verdict.Anomalies,
        Reasons = verdict.Reasons.Count == 0 ? null : string.Join(' ', verdict.Reasons), IsLate = isLate, AppVersion = appVersion, BatteryPercentage = battery, NetworkType = network,
    };
}

/// <summary>Where each vehicle is now: one row per vehicle, so the control tower never reads the history to draw its map.</summary>
[AuditIgnore]
public sealed class CurrentVehiclePosition : Entity, ITenantScoped
{
    private CurrentVehiclePosition()
    {
    }

    public Guid TenantId { get; private set; }

    public string VehicleReference { get; private set; } = null!;

    public Guid? SessionId { get; private set; }

    public Guid? ShipmentId { get; private set; }

    public string? TripReference { get; private set; }

    public string? ShipmentReference { get; private set; }

    public string? DriverName { get; private set; }

    public Guid? TransporterId { get; private set; }

    public string? TransporterReference { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public double? AccuracyMeters { get; private set; }

    public double? SpeedKph { get; private set; }

    public double? Heading { get; private set; }

    public DateTimeOffset LastCapturedAt { get; private set; }

    public DateTimeOffset LastReceivedAt { get; private set; }

    public TrackingHealth Health { get; private set; }

    public bool Moving { get; private set; }

    public int? BatteryPercentage { get; private set; }

    public string? NetworkType { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CurrentVehiclePosition Create(Guid tenantId, string vehicleReference) => new() { TenantId = tenantId, VehicleReference = vehicleReference };

    public void Update(TrackedShipment shipment, TrackingSession session, LocationInput fix, DateTimeOffset receivedAt, bool moving, int? battery, string? network)
    {
        SessionId = session.Id;
        ShipmentId = shipment.ShipmentId;
        TripReference = shipment.TripReference;
        ShipmentReference = shipment.ShipmentReference;
        DriverName = shipment.DriverName ?? session.DriverReference;
        TransporterId = shipment.TransporterId;
        TransporterReference = shipment.TransporterReference;
        Latitude = fix.Latitude;
        Longitude = fix.Longitude;
        AccuracyMeters = fix.AccuracyMeters;
        SpeedKph = fix.SpeedKph;
        Heading = fix.Heading;
        LastCapturedAt = fix.CapturedAt;
        LastReceivedAt = receivedAt;
        Health = TrackingHealth.Healthy;
        Moving = moving;
        BatteryPercentage = battery ?? BatteryPercentage;
        NetworkType = network ?? NetworkType;
        UpdatedAt = receivedAt;
    }

    public void SetHealth(TrackingHealth health, DateTimeOffset at)
    {
        Health = health;
        UpdatedAt = at;
        if (health is TrackingHealth.Completed)
        {
            SessionId = null;
            Moving = false;
        }
    }
}

/// <summary>The phone or device a trip is tracked with, as last seen. Used to spot a device change and to show the driver's side of tracking problems.</summary>
[AuditIgnore]
public sealed class TrackingDevice : Entity, ITenantScoped
{
    private TrackingDevice()
    {
    }

    public Guid TenantId { get; private set; }

    public string DeviceId { get; private set; } = null!;

    public string? DriverReference { get; private set; }

    public string? AppVersion { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public int? LastBatteryPercentage { get; private set; }

    public string? LastNetworkType { get; private set; }

    /// <summary>Granted, Denied or Unknown, as the driver app last reported it.</summary>
    public string? LocationPermission { get; private set; }

    public static TrackingDevice Create(Guid tenantId, string deviceId) => new() { TenantId = tenantId, DeviceId = deviceId };

    public void Seen(string? driver, string? appVersion, int? battery, string? network, string? permission, DateTimeOffset at)
    {
        DriverReference = driver ?? DriverReference;
        AppVersion = appVersion ?? AppVersion;
        LastBatteryPercentage = battery ?? LastBatteryPercentage;
        LastNetworkType = network ?? LastNetworkType;
        LocationPermission = permission ?? LocationPermission;
        LastSeenAt = at;
    }
}

/// <summary>One thing that happened on a trip, with when, where and who or what said so. The timeline is built from these.</summary>
public sealed class ShipmentEvent : Entity, ITenantScoped
{
    private ShipmentEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string ShipmentReference { get; private set; } = null!;

    public string TripReference { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public DateTimeOffset EventTime { get; private set; }

    public EventSource Source { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? GeofenceReference { get; private set; }

    public Guid? StopId { get; private set; }

    public string? ReasonCode { get; private set; }

    public double? Confidence { get; private set; }

    public Guid? PerformedBy { get; private set; }

    public static ShipmentEvent Create(
        TrackedShipment shipment, string type, string description, DateTimeOffset at, EventSource source, double? lat = null, double? lon = null, string? geofence = null, Guid? stopId = null,
        string? reason = null, double? confidence = null, Guid? by = null) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, ShipmentReference = shipment.ShipmentReference, TripReference = shipment.TripReference, EventType = type, Description = description,
        EventTime = at, Source = source, Latitude = lat, Longitude = lon, GeofenceReference = geofence, StopId = stopId, ReasonCode = reason, Confidence = confidence, PerformedBy = by,
    };
}

public static class ShipmentEventTypes
{
    public const string Dispatched = "Dispatched";
    public const string InTransit = "InTransit";
    public const string TrackingStarted = "TrackingStarted";
    public const string TrackingStopped = "TrackingStopped";
    public const string TrackingCompleted = "TrackingCompleted";
    public const string TrackingStale = "TrackingStale";
    public const string TrackingLost = "TrackingLost";
    public const string TrackingResumed = "TrackingResumed";
    public const string ArrivedStop = "ArrivedStop";
    public const string DepartedStop = "DepartedStop";
    public const string ApproachingStop = "ApproachingStop";
    public const string EnteredGeofence = "EnteredGeofence";
    public const string ExitedGeofence = "ExitedGeofence";
    public const string StayedInGeofence = "StayedInGeofence";
    public const string RouteDeviation = "RouteDeviation";
    public const string RouteResumed = "RouteResumed";
    public const string ExcessiveDwell = "ExcessiveDwell";
    public const string UnplannedStop = "UnplannedStop";
    public const string EtaUpdated = "EtaUpdated";
    public const string RiskChanged = "RiskChanged";
    public const string Delivered = "Delivered";
    public const string ManualMilestone = "ManualMilestone";
    public const string EtaOverride = "EtaOverride";
    public const string StatusOverride = "StatusOverride";
    public const string GpsAnomaly = "GpsAnomaly";
    public const string DelayReason = "DelayReason";
}

/// <summary>A step a trip is expected to go through, with when it was planned, when it is now expected and when it really happened: three separate times, never written over each other.</summary>
public sealed class Milestone : Entity, ITenantScoped
{
    private Milestone()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public Guid? StopId { get; private set; }

    public MilestoneType Type { get; private set; }

    public string Label { get; private set; } = null!;

    public int Order { get; private set; }

    public DateTimeOffset? PlannedAt { get; private set; }

    public DateTimeOffset? EstimatedAt { get; private set; }

    public DateTimeOffset? ActualAt { get; private set; }

    public MilestoneStatus Status { get; private set; }

    public EventSource Source { get; private set; }

    public string? ReasonCode { get; private set; }

    public static Milestone Create(TrackedShipment shipment, MilestoneType type, string label, int order, Guid? stopId, DateTimeOffset? planned) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, Type = type, Label = label, Order = order, StopId = stopId, PlannedAt = planned, Status = MilestoneStatus.Pending, Source = EventSource.Planning,
    };

    public void Estimate(DateTimeOffset at)
    {
        if (Status is MilestoneStatus.Pending or MilestoneStatus.Estimated)
        {
            EstimatedAt = at;
            Status = MilestoneStatus.Estimated;
        }
    }

    public void Achieve(DateTimeOffset at, EventSource source, string? reason = null)
    {
        ActualAt ??= at;
        Status = MilestoneStatus.Achieved;
        Source = source;
        ReasonCode = reason ?? ReasonCode;
    }

    public void Skip() { if (Status != MilestoneStatus.Achieved) { Status = MilestoneStatus.Skipped; } }
}

/// <summary>A shared place the tenant cares about: a warehouse, a customer, a hub, a toll plaza, a restricted or high-risk area.</summary>
public sealed class Geofence : AggregateRoot, ITenantScoped
{
    private Geofence()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public GeofenceType Type { get; private set; }

    public double CenterLatitude { get; private set; }

    public double CenterLongitude { get; private set; }

    public int RadiusMeters { get; private set; }

    /// <summary>A closed ring as <c>[[lat, lon], …]</c>. When present the polygon is the shape and the radius is only a hint.</summary>
    public string? PolygonJson { get; private set; }

    public GeofenceStatus Status { get; private set; }

    public DateOnly? EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public bool IsActiveOn(DateOnly day) => Status == GeofenceStatus.Active && (EffectiveFrom is null || day >= EffectiveFrom) && (EffectiveTo is null || day <= EffectiveTo);

    public IReadOnlyList<GeoPoint>? Polygon => PolygonJson is null ? null : TrackingJson.Points(PolygonJson);

    public GeofenceShape Shape => new(new GeoPoint(CenterLatitude, CenterLongitude), RadiusMeters, Polygon);

    public static Result<Geofence> Create(
        Guid tenantId, string code, string name, GeofenceType type, double lat, double lon, int radiusM, IReadOnlyList<GeoPoint>? polygon, DateOnly? from, DateOnly? to)
    {
        var geofence = new Geofence { TenantId = tenantId };
        var applied = geofence.Apply(code, name, type, lat, lon, radiusM, polygon, from, to, GeofenceStatus.Active);
        return applied.IsFailure ? applied.Error : geofence;
    }

    public Result Apply(string code, string name, GeofenceType type, double lat, double lon, int radiusM, IReadOnlyList<GeoPoint>? polygon, DateOnly? from, DateOnly? to, GeofenceStatus status)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30)
        {
            errors["code"] = ["Give a code of up to 30 characters."];
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
        {
            errors["name"] = ["Give a name of up to 150 characters."];
        }

        if (!Geo.IsValid(lat, lon))
        {
            errors["centerLatitude"] = ["The centre is not a real position."];
        }

        if (polygon is null && radiusM is < 20 or > 50_000)
        {
            errors["radiusMeters"] = ["The radius must be between 20 m and 50 km."];
        }

        if (polygon is not null && (polygon.Count < 3 || polygon.Count > 500 || polygon.Any(p => !Geo.IsValid(p.Latitude, p.Longitude))))
        {
            errors["polygon"] = ["A polygon needs between 3 and 500 real positions."];
        }

        if (from is not null && to is not null && to < from)
        {
            errors["effectiveTo"] = ["The end cannot be before the start."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("geofences.invalid", "The geofence is not valid.") with { ValidationErrors = errors };
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
        CenterLatitude = lat;
        CenterLongitude = lon;
        RadiusMeters = polygon is null ? radiusM : Math.Max(radiusM, 20);
        PolygonJson = polygon is null ? null : TrackingJson.Points(polygon);
        EffectiveFrom = from;
        EffectiveTo = to;
        Status = status;
        return Result.Success();
    }
}

/// <summary>Where one trip stands relative to one place, remembered between locations so entering and leaving can be confirmed.</summary>
[AuditIgnore]
public sealed class GeofencePresence : Entity, ITenantScoped
{
    private GeofencePresence()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    /// <summary>The stop's id, or the shared geofence's id, whichever this presence is about.</summary>
    public Guid SubjectId { get; private set; }

    public bool IsStop { get; private set; }

    public Presence State { get; private set; }

    public DateTimeOffset? Since { get; private set; }

    public int Points { get; private set; }

    public DateTimeOffset? InsideSince { get; private set; }

    public bool StayedRaised { get; private set; }

    public static GeofencePresence Create(Guid tenantId, Guid trackedShipmentId, Guid subjectId, bool isStop) =>
        new() { TenantId = tenantId, TrackedShipmentId = trackedShipmentId, SubjectId = subjectId, IsStop = isStop, State = Presence.Outside };

    public PresenceState ToState() => new() { State = State, Since = Since, Points = Points, InsideSince = InsideSince, StayedRaised = StayedRaised };

    public void Keep(PresenceState state)
    {
        State = state.State;
        Since = state.Since;
        Points = state.Points;
        InsideSince = state.InsideSince;
        StayedRaised = state.StayedRaised;
    }
}

[AuditIgnore]
public sealed class GeofenceEvent : Entity, ITenantScoped
{
    private GeofenceEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid? GeofenceId { get; private set; }

    public Guid? StopId { get; private set; }

    public string GeofenceCode { get; private set; } = null!;

    public string GeofenceName { get; private set; } = null!;

    public GeofenceType PlaceType { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public string ShipmentReference { get; private set; } = null!;

    public GeofenceEventType EventType { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public double? GpsAccuracy { get; private set; }

    public double Confidence { get; private set; }

    public static GeofenceEvent Create(
        TrackedShipment shipment, Guid? geofenceId, Guid? stopId, string code, string name, GeofenceType type, GeofenceEventType eventType, DateTimeOffset at, double lat, double lon, double? accuracy,
        double confidence) => new()
    {
        TenantId = shipment.TenantId, GeofenceId = geofenceId, StopId = stopId, GeofenceCode = code, GeofenceName = name, PlaceType = type, TrackedShipmentId = shipment.Id, TripReference = shipment.TripReference,
        VehicleReference = shipment.VehicleReference, ShipmentReference = shipment.ShipmentReference, EventType = eventType, DetectedAt = at, Latitude = lat, Longitude = lon, GpsAccuracy = accuracy,
        Confidence = confidence,
    };
}

/// <summary>The route a trip should follow, as a polyline measured when it was received.</summary>
public sealed class RoutePlan : Entity, ITenantScoped
{
    private RoutePlan()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string PointsJson { get; private set; } = "[]";

    public double LengthKm { get; private set; }

    public int? PlannedMinutes { get; private set; }

    public string Source { get; private set; } = "Estimate";

    public DateTimeOffset CreatedAt { get; private set; }

    public static RoutePlan Create(Guid tenantId, Guid trackedShipmentId, IReadOnlyList<GeoPoint> points, int? minutes, string source, DateTimeOffset at) =>
        new() { TenantId = tenantId, TrackedShipmentId = trackedShipmentId, PointsJson = TrackingJson.Points(points), LengthKm = Math.Round(new RouteGeometry(points).LengthKm, 2), PlannedMinutes = minutes, Source = source, CreatedAt = at };

    public IReadOnlyList<GeoPoint> Points => TrackingJson.Points(PointsJson);
}

public sealed class RouteDeviation : Entity, ITenantScoped
{
    private RouteDeviation()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public double DistanceFromRouteKm { get; private set; }

    public int DurationMinutes { get; private set; }

    public Severity Severity { get; private set; }

    public DeviationStatus Status { get; private set; }

    public DelayReason? Reason { get; private set; }

    public string? ReasonNote { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public static RouteDeviation Open(TrackedShipment shipment, DateTimeOffset startedAt, double lat, double lon, double distanceKm, int minutes, Severity severity) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, TripReference = shipment.TripReference, ShipmentReference = shipment.ShipmentReference, VehicleReference = shipment.VehicleReference,
        DetectedAt = startedAt, Latitude = lat, Longitude = lon, DistanceFromRouteKm = Math.Round(distanceKm, 2), DurationMinutes = minutes, Severity = severity, Status = DeviationStatus.Open,
    };

    public void Update(double distanceKm, int minutes, Severity severity)
    {
        DistanceFromRouteKm = Math.Max(DistanceFromRouteKm, Math.Round(distanceKm, 2));
        DurationMinutes = minutes;
        Severity = (Severity)Math.Max((int)Severity, (int)severity);
    }

    public void Resolve(DateTimeOffset at, int minutes)
    {
        Status = DeviationStatus.Resolved;
        ResolvedAt = at;
        DurationMinutes = minutes;
    }

    public void RecordReason(DelayReason reason, string? note)
    {
        Reason = reason;
        ReasonNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

[AuditIgnore]
public sealed class DwellEvent : Entity, ITenantScoped
{
    private DwellEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public Guid? StopId { get; private set; }

    public string? Place { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset? EndAt { get; private set; }

    public int DurationMinutes { get; private set; }

    public DwellKind Kind { get; private set; }

    public int ExpectedDurationMinutes { get; private set; }

    public int ExcessDurationMinutes { get; private set; }

    public DwellStatus Status { get; private set; }

    public static DwellEvent Open(TrackedShipment shipment, Guid? stopId, string? place, double lat, double lon, DateTimeOffset start, DwellKind kind, int expectedMinutes) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, TripReference = shipment.TripReference, ShipmentReference = shipment.ShipmentReference, VehicleReference = shipment.VehicleReference,
        StopId = stopId, Place = place, Latitude = lat, Longitude = lon, StartAt = start, Kind = kind, ExpectedDurationMinutes = expectedMinutes, Status = DwellStatus.Ongoing,
    };

    public void Progress(int minutes, int excessMinutes)
    {
        DurationMinutes = minutes;
        ExcessDurationMinutes = Math.Max(ExcessDurationMinutes, excessMinutes);
    }

    public void Close(DateTimeOffset end, int minutes)
    {
        EndAt = end;
        DurationMinutes = minutes;
        ExcessDurationMinutes = Math.Max(0, minutes - ExpectedDurationMinutes);
        Status = DwellStatus.Completed;
    }
}

/// <summary>A stretch when no location arrived. It is a gap in what we know, never a claim that the vehicle stopped.</summary>
[AuditIgnore]
public sealed class TrackingGap : Entity, ITenantScoped
{
    private TrackingGap()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public DateTimeOffset GapStart { get; private set; }

    public DateTimeOffset? GapEnd { get; private set; }

    public int DurationMinutes { get; private set; }

    public double LastKnownLatitude { get; private set; }

    public double LastKnownLongitude { get; private set; }

    public Severity Severity { get; private set; }

    public bool IsOpen => GapEnd is null;

    public static TrackingGap Open(TrackedShipment shipment, DateTimeOffset start, double lat, double lon, Severity severity, int minutes) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, TripReference = shipment.TripReference, ShipmentReference = shipment.ShipmentReference, VehicleReference = shipment.VehicleReference,
        GapStart = start, LastKnownLatitude = lat, LastKnownLongitude = lon, Severity = severity, DurationMinutes = minutes,
    };

    public void Grow(int minutes, Severity severity)
    {
        DurationMinutes = minutes;
        Severity = (Severity)Math.Max((int)Severity, (int)severity);
    }

    public void Close(DateTimeOffset end)
    {
        GapEnd = end;
        DurationMinutes = (int)(end - GapStart).TotalMinutes;
    }
}

/// <summary>An estimate of arrival at one stop at one moment, kept so how good the estimates were can be measured later.</summary>
[AuditIgnore]
public sealed class EtaPrediction : Entity, ITenantScoped
{
    private EtaPrediction()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string ShipmentReference { get; private set; } = null!;

    public string TripReference { get; private set; } = null!;

    public Guid? StopId { get; private set; }

    public bool IsFinalDestination { get; private set; }

    public DateTimeOffset PredictedAt { get; private set; }

    public DateTimeOffset PredictedEta { get; private set; }

    public double RemainingKm { get; private set; }

    public double Confidence { get; private set; }

    public RiskLevel RiskLevel { get; private set; }

    public RiskStatus Risk { get; private set; }

    public int DelayMinutes { get; private set; }

    public string Source { get; private set; } = "RuleBased";

    public string CalculationVersion { get; private set; } = "rules-1";

    public static EtaPrediction Create(TrackedShipment shipment, Guid? stopId, bool isFinal, DateTimeOffset at, StopEta eta) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, ShipmentReference = shipment.ShipmentReference, TripReference = shipment.TripReference, StopId = stopId, IsFinalDestination = isFinal,
        PredictedAt = at, PredictedEta = eta.Eta, RemainingKm = eta.RemainingKm, Confidence = eta.Confidence, RiskLevel = eta.Level, Risk = eta.Risk, DelayMinutes = eta.DelayMinutes,
    };
}

/// <summary>Something worth knowing. It may clear on its own and needs no owner; if it needs action it becomes an exception.</summary>
public sealed class TrackingAlert : AggregateRoot, ITenantScoped
{
    private TrackingAlert()
    {
    }

    public Guid TenantId { get; private set; }

    public AlertType Type { get; private set; }

    public Severity Severity { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public Guid? TransporterId { get; private set; }

    public string Message { get; private set; } = null!;

    public AlertStatus Status { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    /// <summary>Identifies the condition this alert is about, so the same condition is never alerted twice.</summary>
    public string DedupeKey { get; private set; } = null!;

    public Guid? AcknowledgedBy { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolutionNote { get; private set; }

    public Guid? ExceptionId { get; private set; }

    public static TrackingAlert Raise(TrackedShipment shipment, AlertType type, Severity severity, string message, string dedupeKey, DateTimeOffset at, int dueMinutes) => new()
    {
        TenantId = shipment.TenantId, Type = type, Severity = severity, TrackedShipmentId = shipment.Id, TripReference = shipment.TripReference, ShipmentReference = shipment.ShipmentReference,
        VehicleReference = shipment.VehicleReference, TransporterId = shipment.TransporterId, Message = message, Status = AlertStatus.Open, RaisedAt = at, DueAt = at.AddMinutes(dueMinutes), DedupeKey = dedupeKey,
    };

    public void Worsen(Severity severity, string message)
    {
        if (severity > Severity)
        {
            Severity = severity;
        }

        Message = message;
    }

    public void LinkException(Guid exceptionId) => ExceptionId = exceptionId;

    public Result Acknowledge(Guid? by, DateTimeOffset at)
    {
        if (Status == AlertStatus.Resolved)
        {
            return Error.Conflict("tracking.alert_resolved", "This alert is already resolved.");
        }

        Status = AlertStatus.Acknowledged;
        AcknowledgedBy = by;
        AcknowledgedAt = at;
        return Result.Success();
    }

    public Result Resolve(string? note, DateTimeOffset at)
    {
        if (Status == AlertStatus.Resolved)
        {
            return Error.Conflict("tracking.alert_resolved", "This alert is already resolved.");
        }

        Status = AlertStatus.Resolved;
        ResolvedAt = at;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Result.Success();
    }
}

public sealed class TrackingExceptionNote : Entity, ITenantScoped
{
    private TrackingExceptionNote()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ExceptionId { get; private set; }

    public string Text { get; private set; } = null!;

    public Guid? By { get; private set; }

    public DateTimeOffset At { get; private set; }

    internal static TrackingExceptionNote Create(Guid tenantId, Guid exceptionId, string text, Guid? by, DateTimeOffset at) =>
        new() { TenantId = tenantId, ExceptionId = exceptionId, Text = text.Trim(), By = by, At = at };
}

/// <summary>A problem somebody has to own: it has an owner, a due time, an escalation path and a recorded outcome.</summary>
public sealed class TrackingException : AggregateRoot, ITenantScoped
{
    private readonly List<TrackingExceptionNote> _notes = [];

    private TrackingException()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public AlertType Type { get; private set; }

    public Severity Severity { get; private set; }

    public string Description { get; private set; } = null!;

    public Guid TrackedShipmentId { get; private set; }

    public string TripReference { get; private set; } = null!;

    public string ShipmentReference { get; private set; } = null!;

    public string? VehicleReference { get; private set; }

    public Guid? TransporterId { get; private set; }

    public string? TransporterReference { get; private set; }

    public ExceptionStatus Status { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public string? Department { get; private set; }

    public int EscalationLevel { get; private set; }

    public string? EscalatedTo { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    public string? RootCause { get; private set; }

    public DelayReason? DelayReason { get; private set; }

    public string? ActionTaken { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The condition that raised this has gone away by itself (the vehicle is reporting again, back on route). It still needs an owner to close it.</summary>
    public DateTimeOffset? ConditionClearedAt { get; private set; }

    public Guid? AlertId { get; private set; }

    public IReadOnlyList<TrackingExceptionNote> Notes => _notes;

    public bool IsOpen => Status is not (ExceptionStatus.Resolved or ExceptionStatus.Closed);

    public static TrackingException Raise(string number, TrackedShipment shipment, TrackingAlert alert, DateTimeOffset at, int dueMinutes)
    {
        var exception = new TrackingException
        {
            TenantId = shipment.TenantId, Number = number, Type = alert.Type, Severity = alert.Severity, Description = alert.Message, TrackedShipmentId = shipment.Id, TripReference = shipment.TripReference,
            ShipmentReference = shipment.ShipmentReference, VehicleReference = shipment.VehicleReference, TransporterId = shipment.TransporterId, TransporterReference = shipment.TransporterReference,
            Status = ExceptionStatus.Open, RaisedAt = at, DueAt = at.AddMinutes(dueMinutes), AlertId = alert.Id,
        };
        exception._notes.Add(TrackingExceptionNote.Create(shipment.TenantId, exception.Id, "Raised.", null, at));
        return exception;
    }

    public Result Acknowledge(Guid? by, DateTimeOffset at)
    {
        if (Status != ExceptionStatus.Open)
        {
            return Error.Conflict("tracking.exception_state", $"An exception that is {Status} cannot be acknowledged.");
        }

        Status = ExceptionStatus.Acknowledged;
        OwnerUserId ??= by;
        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, "Acknowledged.", by, at));
        return Result.Success();
    }

    public Result Assign(Guid? owner, string? department, DateTimeOffset? dueAt, Severity? severity, Guid? by, DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return Error.Conflict("tracking.exception_state", $"An exception that is {Status} cannot be assigned.");
        }

        if (owner is null && string.IsNullOrWhiteSpace(department))
        {
            return Error.Validation("tracking.owner_required", "Give an owner or a department.");
        }

        OwnerUserId = owner ?? OwnerUserId;
        Department = string.IsNullOrWhiteSpace(department) ? Department : department.Trim();
        DueAt = dueAt ?? DueAt;
        Severity = severity ?? Severity;
        if (Status == ExceptionStatus.Open)
        {
            Status = ExceptionStatus.InProgress;
        }

        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, $"Assigned{(Department is null ? string.Empty : $" to {Department}")}.", by, at));
        return Result.Success();
    }

    public Result Start(Guid? by, DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return Error.Conflict("tracking.exception_state", $"An exception that is {Status} cannot be worked.");
        }

        Status = ExceptionStatus.InProgress;
        OwnerUserId ??= by;
        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, "Work started.", by, at));
        return Result.Success();
    }

    public Result Escalate(string reason, string? level, Guid? by, DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return Error.Conflict("tracking.exception_state", $"An exception that is {Status} cannot be escalated.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("tracking.reason_required", "Say why it is being escalated.");
        }

        Status = ExceptionStatus.Escalated;
        EscalationLevel++;
        EscalatedTo = level ?? EscalatedTo;
        EscalatedAt = at;
        if (Severity < Severity.High)
        {
            Severity = Severity.High;
        }

        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, $"Escalated{(level is null ? string.Empty : $" to {level}")}: {reason.Trim()}", by, at));
        return Result.Success();
    }

    public Result AddNote(string text, Guid? by, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Error.Validation("tracking.note_required", "Write the note.");
        }

        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, text, by, at));
        return Result.Success();
    }

    public Result Resolve(string rootCause, DelayReason? delayReason, string? actionTaken, Guid? by, DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return Error.Conflict("tracking.exception_state", $"An exception that is {Status} cannot be resolved.");
        }

        if (string.IsNullOrWhiteSpace(rootCause))
        {
            return Error.Validation("tracking.root_cause_required", "Record the cause before resolving.");
        }

        Status = ExceptionStatus.Resolved;
        RootCause = rootCause.Trim();
        DelayReason = delayReason;
        ActionTaken = string.IsNullOrWhiteSpace(actionTaken) ? ActionTaken : actionTaken.Trim();
        ResolvedAt = at;
        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, $"Resolved: {RootCause}", by, at));
        return Result.Success();
    }

    public Result Close(Guid? by, DateTimeOffset at)
    {
        if (Status != ExceptionStatus.Resolved)
        {
            return Error.Conflict("tracking.exception_state", "Resolve the exception before closing it.");
        }

        Status = ExceptionStatus.Closed;
        ClosedAt = at;
        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, "Closed.", by, at));
        return Result.Success();
    }

    public void ConditionCleared(DateTimeOffset at)
    {
        if (IsOpen && ConditionClearedAt is null)
        {
            ConditionClearedAt = at;
            _notes.Add(TrackingExceptionNote.Create(TenantId, Id, "The condition has cleared by itself; it still needs to be closed.", null, at));
        }
    }

    public void EscalateAutomatically(string level, DateTimeOffset at)
    {
        EscalationLevel++;
        EscalatedTo = level;
        EscalatedAt = at;
        Status = ExceptionStatus.Escalated;
        _notes.Add(TrackingExceptionNote.Create(TenantId, Id, $"Escalated to {level}: not resolved in time.", null, at));
    }
}

/// <summary>A link a customer can follow to see where a shipment is. Only a hash of the secret is kept, so a stolen database cannot reveal working links.</summary>
public sealed class CustomerTrackingLink : AggregateRoot, ITenantScoped
{
    private CustomerTrackingLink()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public string ShipmentReference { get; private set; } = null!;

    public string? CustomerReference { get; private set; }

    public string? CustomerName { get; private set; }

    [AuditIgnore]
    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    [AuditIgnore]
    public int ViewCount { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastViewedAt { get; private set; }

    public LinkStatus StatusAt(DateTimeOffset now) => RevokedAt is not null ? LinkStatus.Revoked : now >= ExpiresAt ? LinkStatus.Expired : LinkStatus.Active;

    public static CustomerTrackingLink Create(TrackedShipment shipment, string? customerReference, string? customerName, string tokenHash, DateTimeOffset expiresAt) => new()
    {
        TenantId = shipment.TenantId, TrackedShipmentId = shipment.Id, ShipmentReference = shipment.ShipmentReference, CustomerReference = customerReference, CustomerName = customerName,
        TokenHash = tokenHash, ExpiresAt = expiresAt,
    };

    public void Revoke(Guid? by, DateTimeOffset at)
    {
        RevokedAt ??= at;
        RevokedBy ??= by;
    }

    public void Viewed(DateTimeOffset at)
    {
        ViewCount++;
        LastViewedAt = at;
    }
}

/// <summary>One command from a device, remembered by the key the device chose, so a retry returns the first answer and changes nothing.</summary>
[AuditIgnore]
public sealed class TrackingSyncRecord : Entity, ITenantScoped
{
    private TrackingSyncRecord()
    {
    }

    public Guid TenantId { get; private set; }

    public string ClientKey { get; private set; } = null!;

    public string Operation { get; private set; } = null!;

    public string? DeviceId { get; private set; }

    public string? TripReference { get; private set; }

    public string? ResultJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TrackingSyncRecord Create(Guid tenantId, string clientKey, string operation, string? deviceId, string? tripReference, string? resultJson, DateTimeOffset at) =>
        new() { TenantId = tenantId, ClientKey = clientKey, Operation = operation, DeviceId = deviceId, TripReference = tripReference, ResultJson = resultJson, CreatedAt = at };
}

public static class TrackingJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static string Points(IReadOnlyList<GeoPoint> points) => System.Text.Json.JsonSerializer.Serialize(points.Select(p => new[] { Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6) }), Options);

    public static IReadOnlyList<GeoPoint> Points(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<double[][]>(json, Options)?.Where(p => p.Length >= 2).Select(p => new GeoPoint(p[0], p[1])).ToList() ?? [];
}
