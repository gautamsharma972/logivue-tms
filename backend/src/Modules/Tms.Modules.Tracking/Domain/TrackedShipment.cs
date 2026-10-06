using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;

namespace Tms.Modules.Tracking.Domain;

/// <summary>A stop on a tracked trip, with where it is, when it should be reached, and what actually happened there.</summary>
[AuditIgnore]
public sealed class ShipmentStop : Entity, ITenantScoped
{
    private ShipmentStop()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TrackedShipmentId { get; private set; }

    public int Sequence { get; private set; }

    public StopKind Kind { get; private set; }

    public string Name { get; private set; } = null!;

    public string? City { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public int RadiusM { get; private set; }

    /// <summary>A shared geofence that outlines this place more exactly than a circle around its coordinates.</summary>
    public Guid? GeofenceId { get; private set; }

    public GeofenceType PlaceType { get; private set; }

    public Guid? OrderId { get; private set; }

    public string? Reference { get; private set; }

    public string? CustomerReference { get; private set; }

    public string? CustomerName { get; private set; }

    public DateTimeOffset? PlannedArrival { get; private set; }

    public DateTimeOffset? WindowStart { get; private set; }

    public DateTimeOffset? WindowEnd { get; private set; }

    public int? ExpectedDwellMinutes { get; private set; }

    /// <summary>Where the stop lies along the planned route, measured once when the route is known.</summary>
    public double? AlongKm { get; private set; }

    public StopStatus Status { get; private set; }

    public DateTimeOffset? ArrivedAt { get; private set; }

    public DateTimeOffset? DepartedAt { get; private set; }

    public DateTimeOffset? EtaAt { get; private set; }

    public double? EtaConfidence { get; private set; }

    public int DelayMinutes { get; private set; }

    public RiskStatus Risk { get; private set; }

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    public GeoPoint? Point => HasLocation ? new GeoPoint(Latitude!.Value, Longitude!.Value) : null;

    internal static ShipmentStop Create(Guid tenantId, Guid trackedShipmentId, TrackingStopFact fact, int defaultRadiusM) => new()
    {
        TenantId = tenantId, TrackedShipmentId = trackedShipmentId, Sequence = fact.Sequence, Kind = string.Equals(fact.Kind, "Pickup", StringComparison.OrdinalIgnoreCase) ? StopKind.Pickup : StopKind.Drop,
        Name = fact.Name, City = fact.City, Latitude = fact.Latitude, Longitude = fact.Longitude, RadiusM = defaultRadiusM, OrderId = fact.OrderId, Reference = fact.Reference,
        CustomerReference = fact.CustomerReference, CustomerName = fact.CustomerName, PlannedArrival = fact.PlannedArrival, WindowStart = fact.WindowStart, WindowEnd = fact.WindowEnd,
        ExpectedDwellMinutes = fact.ExpectedDwellMinutes, Status = StopStatus.Pending,
        PlaceType = string.Equals(fact.Kind, "Pickup", StringComparison.OrdinalIgnoreCase) ? GeofenceType.Origin : GeofenceType.Customer,
    };

    internal void SetAlong(double? alongKm) => AlongKm = alongKm;

    internal void UseGeofence(Guid geofenceId, GeofenceType type)
    {
        GeofenceId = geofenceId;
        PlaceType = type;
    }

    internal void Arrive(DateTimeOffset at)
    {
        Status = StopStatus.Arrived;
        ArrivedAt ??= at;
    }

    internal void Depart(DateTimeOffset at)
    {
        Status = StopStatus.Departed;
        ArrivedAt ??= at;
        DepartedAt = at;
    }

    internal void Approach()
    {
        if (Status == StopStatus.Pending)
        {
            Status = StopStatus.Approaching;
        }
    }

    internal void Skip() => Status = StopStatus.Skipped;

    internal void Estimate(StopEta eta)
    {
        EtaAt = eta.Eta;
        EtaConfidence = eta.Confidence;
        DelayMinutes = eta.DelayMinutes;
        Risk = eta.Risk;
    }

    /// <summary>The stop that follows, for dwell kind and display.</summary>
    public string PlaceKindLabel => PlaceType.ToString();
}

/// <summary>
/// One trip being followed: who carries it, where it should go, how it is doing. This is the read model the control tower works from, so it is kept up to date as locations arrive and
/// nothing has to read raw GPS history to answer "how is this shipment". Planned, estimated and actual times are separate fields and are never written over each other.
/// </summary>
public sealed class TrackedShipment : AggregateRoot, ITenantScoped
{
    private readonly List<ShipmentStop> _stops = [];

    private TrackedShipment()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string ShipmentReference { get; private set; } = null!;

    public string TripReference { get; private set; } = null!;

    public Guid? TransporterId { get; private set; }

    public string? TransporterReference { get; private set; }

    public string? VehicleReference { get; private set; }

    public string? DriverName { get; private set; }

    public string? DriverPhone { get; private set; }

    public string? CustomerName { get; private set; }

    public string? OriginName { get; private set; }

    public string? DestinationName { get; private set; }

    public ExecutionStatus Execution { get; private set; }

    public TrackingHealth Tracking { get; private set; }

    public RiskStatus Risk { get; private set; }

    public TrackedDeliveryStatus Delivery { get; private set; }

    public DateTimeOffset? PlannedStartAt { get; private set; }

    public DateTimeOffset? PlannedArrivalAt { get; private set; }

    public decimal? PlannedDistanceKm { get; private set; }

    public int? PlannedDurationMinutes { get; private set; }

    public string RouteSource { get; private set; } = "Estimate";

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? CurrentSessionId { get; private set; }

    public IReadOnlyList<ShipmentStop> Stops => _stops;

    // ---- live state: changes with every location, so none of it is audited (the events, alerts and exceptions are the record of what mattered)

    [AuditIgnore]
    public double? LastLatitude { get; private set; }

    [AuditIgnore]
    public double? LastLongitude { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastCapturedAt { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastReceivedAt { get; private set; }

    [AuditIgnore]
    public double? LastSpeedKph { get; private set; }

    [AuditIgnore]
    public double? LastHeading { get; private set; }

    [AuditIgnore]
    public double? LastAccuracyM { get; private set; }

    [AuditIgnore]
    public double TravelledKm { get; private set; }

    [AuditIgnore]
    public double? RemainingKm { get; private set; }

    [AuditIgnore]
    public double? ProgressPct { get; private set; }

    [AuditIgnore]
    public double? AlongKm { get; private set; }

    [AuditIgnore]
    public double? OffRouteKm { get; private set; }

    /// <summary>What the calculation says. It is never overwritten by an operator's correction: that goes in <see cref="EtaOverrideAt"/>.</summary>
    [AuditIgnore]
    public DateTimeOffset? SystemEtaAt { get; private set; }

    [AuditIgnore]
    public double? EtaConfidence { get; private set; }

    [AuditIgnore]
    public int DelayMinutes { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastEtaRecordedAt { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastRecordedEta { get; private set; }

    [AuditIgnore]
    public int IdenticalCount { get; private set; }

    /// <summary>A running average of the speeds seen while moving, weighted to the recent ones.</summary>
    [AuditIgnore]
    public double? RecentMovingSpeedKph { get; private set; }

    [AuditIgnore]
    public int RecentSpeedCount { get; private set; }

    // ---- deviation and dwell memory

    [AuditIgnore]
    public DateTimeOffset? OffRouteSince { get; private set; }

    [AuditIgnore]
    public int OffRoutePoints { get; private set; }

    [AuditIgnore]
    public double MaxOffRouteKm { get; private set; }

    [AuditIgnore]
    public bool DeviationOpen { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? DeviationBackSince { get; private set; }

    [AuditIgnore]
    public double? DwellAnchorLatitude { get; private set; }

    [AuditIgnore]
    public double? DwellAnchorLongitude { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? DwellSince { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? DwellLastAt { get; private set; }

    [AuditIgnore]
    public bool DwellOpen { get; private set; }

    [AuditIgnore]
    public bool DwellPlanned { get; private set; }

    [AuditIgnore]
    public string? DwellWhere { get; private set; }

    [AuditIgnore]
    public bool DwellExcessRaised { get; private set; }

    [AuditIgnore]
    public bool DwellUnplannedRaised { get; private set; }

    // ---- operator corrections, kept beside what the system calculated

    public DateTimeOffset? EtaOverrideAt { get; private set; }

    public string? EtaOverrideReason { get; private set; }

    public Guid? EtaOverrideBy { get; private set; }

    public DateTimeOffset? EtaOverrideSetAt { get; private set; }

    public DelayReason? DelayReason { get; private set; }

    public string? DelayNote { get; private set; }

    /// <summary>The arrival to show people: the operator's correction if there is one, else the calculation.</summary>
    public DateTimeOffset? CurrentEtaAt => EtaOverrideAt ?? SystemEtaAt;

    public bool IsActive => Execution is not (ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Delivered);

    public static TrackedShipment Create(Guid tenantId, PlannedTrackingContext plan, int defaultRadiusM)
    {
        var tracked = new TrackedShipment
        {
            TenantId = tenantId, ShipmentId = plan.ShipmentId, ShipmentReference = plan.ShipmentReference, TripReference = plan.TripReference, TransporterId = plan.TransporterId,
            TransporterReference = plan.TransporterReference, VehicleReference = plan.VehicleReference, DriverName = plan.DriverName, DriverPhone = plan.DriverPhone,
            Execution = ExecutionStatus.Planned, Tracking = TrackingHealth.NotStarted, Risk = RiskStatus.Unknown, Delivery = TrackedDeliveryStatus.Pending,
            PlannedStartAt = plan.PlannedStart, PlannedDistanceKm = plan.PlannedDistanceKm, PlannedDurationMinutes = plan.PlannedDurationMinutes, RouteSource = plan.RouteSource,
        };

        foreach (var fact in plan.Stops.OrderBy(s => s.Sequence))
        {
            tracked._stops.Add(ShipmentStop.Create(tenantId, tracked.Id, fact, defaultRadiusM));
        }

        var first = tracked._stops.FirstOrDefault(s => s.Kind == StopKind.Pickup) ?? tracked._stops.FirstOrDefault();
        var last = tracked._stops.LastOrDefault(s => s.Kind == StopKind.Drop) ?? tracked._stops.LastOrDefault();
        tracked.OriginName = first?.City ?? first?.Name;
        tracked.DestinationName = last?.City ?? last?.Name;
        tracked.CustomerName = last?.CustomerName;
        tracked.PlannedArrivalAt = last?.WindowEnd ?? last?.PlannedArrival;
        return tracked;
    }

    /// <summary>Planning's plan changed (new vehicle or driver, new times). Stops already reached keep what happened to them.</summary>
    public void RefreshPlan(PlannedTrackingContext plan)
    {
        TransporterId = plan.TransporterId;
        TransporterReference = plan.TransporterReference;
        VehicleReference = plan.VehicleReference ?? VehicleReference;
        DriverName = plan.DriverName ?? DriverName;
        DriverPhone = plan.DriverPhone ?? DriverPhone;
        PlannedStartAt = plan.PlannedStart ?? PlannedStartAt;
        PlannedDistanceKm = plan.PlannedDistanceKm ?? PlannedDistanceKm;
        PlannedDurationMinutes = plan.PlannedDurationMinutes ?? PlannedDurationMinutes;
    }

    public void Start(Guid sessionId, DateTimeOffset at, string? vehicleReference, string? driverName)
    {
        CurrentSessionId = sessionId;
        StartedAt ??= at;
        VehicleReference = string.IsNullOrWhiteSpace(vehicleReference) ? VehicleReference : vehicleReference;
        DriverName = string.IsNullOrWhiteSpace(driverName) ? DriverName : driverName;
        Tracking = TrackingHealth.Healthy;
        if (Execution == ExecutionStatus.Planned)
        {
            Execution = ExecutionStatus.EnRouteToOrigin;
        }

        Publish(new TrackingStarted(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, at));
    }

    public void StopTracking(DateTimeOffset at, string reason, bool completed)
    {
        CurrentSessionId = null;
        Tracking = completed ? TrackingHealth.Completed : TrackingHealth.NotStarted;
        if (completed)
        {
            CompletedAt = at;
            if (Execution is not (ExecutionStatus.Delivered or ExecutionStatus.Cancelled))
            {
                Execution = ExecutionStatus.Completed;
            }

            Publish(new TrackingCompleted(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, at, (decimal)Math.Round(TravelledKm, 1)));
        }

        Publish(new TrackingStopped(TenantId, ShipmentId, ShipmentReference, TripReference, VehicleReference, TransporterId, at, reason));
    }

    public void SetHealth(TrackingHealth health) => Tracking = health;

    public void SetExecution(ExecutionStatus status) => Execution = status;

    public void SetDelivery(TrackedDeliveryStatus status) => Delivery = status;

    public void Cancel(DateTimeOffset at)
    {
        Execution = ExecutionStatus.Cancelled;
        CompletedAt ??= at;
        CurrentSessionId = null;
        Tracking = TrackingHealth.Completed;
    }

    /// <summary>A position that was accepted: where the vehicle is now, and how far along its route.</summary>
    public void MoveTo(LocationInput fix, DateTimeOffset receivedAt, double travelledKmDelta, double? alongKm, double? offRouteKm, double? remainingKm, double? progressPct, int identicalCount)
    {
        LastLatitude = fix.Latitude;
        LastLongitude = fix.Longitude;
        LastCapturedAt = fix.CapturedAt;
        LastReceivedAt = receivedAt;
        LastSpeedKph = fix.SpeedKph;
        LastHeading = fix.Heading;
        LastAccuracyM = fix.AccuracyMeters;
        TravelledKm += travelledKmDelta;
        AlongKm = alongKm ?? AlongKm;
        OffRouteKm = offRouteKm;
        RemainingKm = remainingKm ?? RemainingKm;
        ProgressPct = progressPct ?? ProgressPct;
        IdenticalCount = identicalCount;
        if (fix.SpeedKph is { } speed && speed >= 8)
        {
            RecentMovingSpeedKph = RecentMovingSpeedKph is { } known ? known * 0.7 + speed * 0.3 : speed;
            RecentSpeedCount = Math.Min(RecentSpeedCount + 1, 10);
        }
    }

    public void SetEstimate(DateTimeOffset? eta, double? confidence, int delayMinutes, RiskStatus risk, DateTimeOffset now)
    {
        SystemEtaAt = eta;
        EtaConfidence = confidence;
        DelayMinutes = delayMinutes;
        Risk = risk;
        LastEtaRecordedAt = now;
        LastRecordedEta = eta;
    }

    public void MarkEtaSeen(DateTimeOffset now) => LastEtaRecordedAt = now;

    public void SetProgress(double? remainingKm, double? progressPct)
    {
        RemainingKm = remainingKm;
        ProgressPct = progressPct;
    }

    public void RecordRisk(RiskStatus risk) => Risk = risk;

    public void OverrideEta(DateTimeOffset eta, string reason, Guid? by, DateTimeOffset now)
    {
        EtaOverrideAt = eta;
        EtaOverrideReason = reason.Trim();
        EtaOverrideBy = by;
        EtaOverrideSetAt = now;
    }

    public void ClearEtaOverride()
    {
        EtaOverrideAt = null;
        EtaOverrideReason = null;
        EtaOverrideBy = null;
        EtaOverrideSetAt = null;
    }

    public void SetDelayReason(DelayReason? reason, string? note)
    {
        DelayReason = reason;
        DelayNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public DeviationState DeviationMemory => new() { OffRouteSince = OffRouteSince, OffPoints = OffRoutePoints, MaxOffKm = MaxOffRouteKm, IsOpen = DeviationOpen, BackSince = DeviationBackSince };

    public void KeepDeviation(DeviationState state)
    {
        OffRouteSince = state.OffRouteSince;
        OffRoutePoints = state.OffPoints;
        MaxOffRouteKm = state.MaxOffKm;
        DeviationOpen = state.IsOpen;
        DeviationBackSince = state.BackSince;
    }

    public DwellState DwellMemory => new()
    {
        Anchor = DwellAnchorLatitude is { } lat && DwellAnchorLongitude is { } lon ? new GeoPoint(lat, lon) : null, Since = DwellSince, LastStationaryAt = DwellLastAt, Open = DwellOpen,
        PlannedStop = DwellPlanned, Where = DwellWhere, ExcessRaised = DwellExcessRaised, UnplannedRaised = DwellUnplannedRaised,
    };

    public void KeepDwell(DwellState state)
    {
        DwellAnchorLatitude = state.Anchor?.Latitude;
        DwellAnchorLongitude = state.Anchor?.Longitude;
        DwellSince = state.Since;
        DwellLastAt = state.LastStationaryAt;
        DwellOpen = state.Open;
        DwellPlanned = state.PlannedStop;
        DwellWhere = state.Where;
        DwellExcessRaised = state.ExcessRaised;
        DwellUnplannedRaised = state.UnplannedRaised;
    }

    public void SetStopAlong(IReadOnlyDictionary<Guid, double?> along)
    {
        foreach (var stop in _stops.Where(s => along.ContainsKey(s.Id)))
        {
            stop.SetAlong(along[stop.Id]);
        }
    }

    /// <summary>Raises an event on this trip so it goes out through the transactional outbox with the rest of the save.</summary>
    public void Publish(IDomainEvent trackingEvent) => Raise(trackingEvent);
}
