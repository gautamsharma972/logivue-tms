namespace Tms.Modules.Tracking.Domain;

public enum TrackingSessionStatus
{
    NotStarted = 1,
    Active = 2,
    Paused = 3,
    Stale = 4,
    Lost = 5,
    Completed = 6,
    Cancelled = 7,
}

/// <summary>How well tracking itself is working. A phone that stopped sending is not a truck that stopped moving.</summary>
public enum TrackingHealth
{
    NotStarted = 1,
    Healthy = 2,
    Stale = 3,
    Lost = 4,
    Completed = 5,
}

public enum TrackingSource
{
    MobileApp = 1,
    GpsDevice = 2,
    Telematics = 3,
    TransporterApi = 4,
    Manual = 5,
}

public enum LocationValidation
{
    Valid = 1,

    /// <summary>Stored for the record but never used to move the vehicle, fire a geofence or estimate an arrival.</summary>
    Suspicious = 2,

    /// <summary>Not stored.</summary>
    Rejected = 3,
}

[Flags]
public enum LocationAnomaly
{
    None = 0,
    ImpossibleSpeed = 1,
    LargeJump = 2,
    PoorAccuracy = 4,
    OldTimestamp = 8,
    FutureTimestamp = 16,
    RepeatedCoordinates = 32,
    DeviceTimeMismatch = 64,
    MockLocation = 128,
    ReportedSpeedMismatch = 256,
}

/// <summary>Where the trip is in its life. Separate from tracking health, risk and delivery, which have their own fields.</summary>
public enum ExecutionStatus
{
    Planned = 1,
    EnRouteToOrigin = 2,
    ArrivedOrigin = 3,
    Loading = 4,
    Departed = 5,
    InTransit = 6,
    ApproachingDestination = 7,
    ArrivedDestination = 8,
    Delivered = 9,
    Cancelled = 10,
    Completed = 11,
}

public enum RiskStatus
{
    Unknown = 1,
    OnTime = 2,
    AtRisk = 3,
    Delayed = 4,
    SeverelyDelayed = 5,
}

public enum RiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum TrackedDeliveryStatus
{
    Pending = 1,
    PartlyDelivered = 2,
    Delivered = 3,
}

public enum StopKind
{
    Pickup = 1,
    Drop = 2,
}

public enum StopStatus
{
    Pending = 1,
    Approaching = 2,
    Arrived = 3,
    Departed = 4,
    Skipped = 5,
}

public enum GeofenceType
{
    Origin = 1,
    Destination = 2,
    Customer = 3,
    Warehouse = 4,
    Hub = 5,
    CrossDock = 6,
    Depot = 7,
    Toll = 8,
    RestrictedArea = 9,
    HighRiskZone = 10,
    Custom = 11,
}

public enum GeofenceStatus
{
    Active = 1,
    Inactive = 2,
}

public enum GeofenceEventType
{
    Entered = 1,
    Exited = 2,
    Stayed = 3,
}

public enum MilestoneType
{
    LoadCreated = 1,
    Tendered = 2,
    Accepted = 3,
    VehicleAssigned = 4,
    VehicleAtOrigin = 5,
    ArrivedOrigin = 6,
    LoadingStarted = 7,
    LoadingCompleted = 8,
    DepartedOrigin = 9,
    InTransit = 10,
    ApproachingStop = 11,
    ArrivedStop = 12,
    Unloading = 13,
    DepartedStop = 14,
    ApproachingDestination = 15,
    ArrivedDestination = 16,
    DeliveryStarted = 17,
    Delivered = 18,
    PodCaptured = 19,
    TrackingCompleted = 20,
}

public enum MilestoneStatus
{
    Pending = 1,
    Estimated = 2,
    Achieved = 3,
    Skipped = 4,
}

/// <summary>Who or what produced an event. The timeline keeps these apart because a driver's tap and a GPS fix are not equally trustworthy.</summary>
public enum EventSource
{
    System = 1,
    Gps = 2,
    Driver = 3,
    Manual = 4,
    Planning = 5,
    Pod = 6,
}

public enum DeviationStatus
{
    Open = 1,
    Resolved = 2,
}

public enum DwellKind
{
    PlannedStop = 1,
    UnplannedStop = 2,
}

public enum DwellStatus
{
    Ongoing = 1,
    Completed = 2,
}

public enum Severity
{
    Informational = 1,
    Warning = 2,
    High = 3,
    Critical = 4,
}

public enum AlertType
{
    TrackingStale = 1,
    TrackingLost = 2,
    RouteDeviation = 3,
    ExcessiveDwell = 4,
    UnplannedStop = 5,
    EtaAtRisk = 6,
    EtaDelayed = 7,
    DeliverySlaRisk = 8,
    GeofenceException = 9,
    GpsAnomaly = 10,
    VehicleStationary = 11,
    GpsUnavailable = 12,
}

public enum AlertStatus
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3,
}

public enum ExceptionStatus
{
    Open = 1,
    Acknowledged = 2,
    InProgress = 3,
    Escalated = 4,
    Resolved = 5,
    Closed = 6,
}

public enum DelayReason
{
    Unknown = 1,
    Traffic = 2,
    VehicleBreakdown = 3,
    WarehouseDelay = 4,
    CustomerDelay = 5,
    LoadingDelay = 6,
    UnloadingDelay = 7,
    Weather = 8,
    RoadClosure = 9,
    RouteDeviation = 10,
    Documentation = 11,
    BorderCheckpost = 12,
    Accident = 13,
    Other = 14,
}

public enum LinkStatus
{
    Active = 1,
    Revoked = 2,
    Expired = 3,
}

public enum SyncStatus
{
    Pending = 1,
    Synced = 2,
    Failed = 3,
}
