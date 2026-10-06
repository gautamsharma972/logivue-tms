using Tms.Modules.Tracking.Domain;

namespace Tms.Modules.Tracking.Application;

public sealed record StopDto(
    Guid Id, int Sequence, StopKind Kind, string Name, string? City, double? Latitude, double? Longitude, int RadiusM, GeofenceType PlaceType, string? Reference, string? CustomerName,
    DateTimeOffset? PlannedArrival, DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, int? ExpectedDwellMinutes, StopStatus Status, DateTimeOffset? ArrivedAt, DateTimeOffset? DepartedAt,
    DateTimeOffset? EtaAt, double? EtaConfidence, int DelayMinutes, RiskStatus Risk, double? AlongKm);

/// <param name="MinutesSinceLastLocation">Age of the last GPS fix, null when there has been none.</param>
/// <param name="EtaAt">The arrival to show: the operator's correction if one was made, else the calculation.</param>
public sealed record TrackedShipmentSummaryDto(
    Guid Id, Guid ShipmentId, string ShipmentReference, string TripReference, Guid? TransporterId, string? TransporterReference, string? VehicleReference, string? DriverName, string? DriverPhone,
    string? CustomerName, string? Origin, string? Destination, ExecutionStatus Execution, TrackingHealth Tracking, RiskStatus Risk, TrackedDeliveryStatus Delivery, DateTimeOffset? PlannedArrivalAt,
    DateTimeOffset? EtaAt, DateTimeOffset? SystemEtaAt, bool EtaOverridden, double? EtaConfidence, int DelayMinutes, double? ProgressPct, double? RemainingKm, double? Latitude, double? Longitude,
    DateTimeOffset? LastCapturedAt, double? SpeedKph, double? Heading, int? MinutesSinceLastLocation, int OpenExceptions, bool OnRoute, bool Moving, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

public sealed record TrackedShipmentDto(
    TrackedShipmentSummaryDto Summary, IReadOnlyList<StopDto> Stops, decimal? PlannedDistanceKm, int? PlannedDurationMinutes, double TravelledKm, double? OffRouteKm, string RouteSource,
    DateTimeOffset? PlannedStartAt, DateTimeOffset? EtaOverrideAt, string? EtaOverrideReason, DelayReason? DelayReason, string? DelayNote, Guid? CurrentSessionId, TrackingSessionStatus? SessionStatus,
    string? DeviceId, int? BatteryPercentage, string? NetworkType, string? LocationPermission, long Version);

/// <param name="Kind">Planned (what was intended), Estimated (what is now expected) or Actual (what happened).</param>
public sealed record TimelineEntryDto(DateTimeOffset At, string Kind, string Label, string? Detail, string? Source, string? Type, double? Latitude, double? Longitude, Guid? StopId, string? Reason);

public sealed record EtaStopDto(Guid StopId, string Name, StopKind Kind, DateTimeOffset? PlannedAt, DateTimeOffset? EtaAt, double? Confidence, int DelayMinutes, RiskStatus Risk, RiskLevel Level, StopStatus Status);

public sealed record EtaHistoryDto(DateTimeOffset PredictedAt, DateTimeOffset Eta, double RemainingKm, double Confidence, RiskLevel RiskLevel, int DelayMinutes, bool IsFinalDestination, Guid? StopId);

public sealed record EtaDto(
    DateTimeOffset? PlannedAt, DateTimeOffset? SystemEtaAt, DateTimeOffset? EtaAt, bool Overridden, DateTimeOffset? OverrideAt, string? OverrideReason, double? Confidence, RiskStatus Risk, RiskLevel Level,
    int DelayMinutes, string CalculationVersion, IReadOnlyList<EtaStopDto> Stops, IReadOnlyList<EtaHistoryDto> History);

public sealed record RouteDto(IReadOnlyList<double[]> Points, double LengthKm, string Source, decimal? PlannedDistanceKm, int? PlannedDurationMinutes, double TravelledKm, double? RemainingKm, double? ProgressPct, IReadOnlyList<StopDto> Stops, IReadOnlyList<RouteDeviationDto> Deviations);

public sealed record RouteDeviationDto(Guid Id, DateTimeOffset DetectedAt, double Latitude, double Longitude, double DistanceFromRouteKm, int DurationMinutes, Severity Severity, DeviationStatus Status, DelayReason? Reason, string? ReasonNote, DateTimeOffset? ResolvedAt);

public sealed record LocationDto(
    Guid Id, double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKph, double? Heading, DateTimeOffset CapturedAt, DateTimeOffset ReceivedAt, LocationValidation Validation, LocationAnomaly Anomalies,
    string? Reasons, bool IsLate, string DeviceId, TrackingSource Source);

public sealed record CurrentLocationDto(
    string? VehicleReference, string? TripReference, string? ShipmentReference, double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKph, double? Heading, DateTimeOffset LastCapturedAt,
    DateTimeOffset LastReceivedAt, TrackingHealth Health, bool Moving, int AgeMinutes, int? BatteryPercentage, string? NetworkType, string? DriverName, string? TransporterReference);

public sealed record VehicleTrackingDto(
    CurrentLocationDto Position, Guid? ShipmentId, string? ShipmentReference, string? TripReference, ExecutionStatus? Execution, RiskStatus? Risk, DateTimeOffset? EtaAt, string? Origin, string? Destination,
    double TodayKm, string? DriverPhone);

public sealed record AlertDto(
    Guid Id, AlertType Type, Severity Severity, AlertStatus Status, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, string Message, DateTimeOffset RaisedAt,
    DateTimeOffset DueAt, bool Overdue, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt, string? ResolutionNote, Guid? ExceptionId);

public sealed record ExceptionNoteDto(DateTimeOffset At, string Text, Guid? By);

public sealed record TrackingExceptionSummaryDto(
    Guid Id, string Number, AlertType Type, Severity Severity, ExceptionStatus Status, Guid ShipmentId, string ShipmentReference, string TripReference, string? VehicleReference, Guid? TransporterId,
    string? TransporterReference, string Description, DateTimeOffset RaisedAt, DateTimeOffset DueAt, bool Overdue, Guid? OwnerUserId, string? Department, int EscalationLevel, string? EscalatedTo,
    bool ConditionCleared, double AgeMinutes);

public sealed record TrackingExceptionDto(
    TrackingExceptionSummaryDto Summary, string? RootCause, DelayReason? DelayReason, string? ActionTaken, DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt, DateTimeOffset? EscalatedAt, string? DriverName,
    string? DriverPhone, double? LastLatitude, double? LastLongitude, DateTimeOffset? LastCapturedAt, IReadOnlyList<ExceptionNoteDto> Notes, long Version);

public sealed record ControlTowerSummaryDto(
    int Active, int OnTime, int AtRisk, int Delayed, int TrackingStale, int TrackingLost, int RouteDeviations, int ExcessDwell, int OpenExceptions, int CompletedToday, int NotStarted, int OpenAlerts,
    DateTimeOffset AsOf);

public sealed record GeofenceDto(
    Guid Id, string Code, string Name, GeofenceType Type, double CenterLatitude, double CenterLongitude, int RadiusMeters, IReadOnlyList<double[]>? Polygon, GeofenceStatus Status, DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo, long Version);

public sealed record SaveGeofenceRequest(
    string Code, string Name, GeofenceType Type, double CenterLatitude, double CenterLongitude, int RadiusMeters, IReadOnlyList<double[]>? Polygon, DateOnly? EffectiveFrom, DateOnly? EffectiveTo,
    GeofenceStatus? Status, long? Version);

public sealed record GeofenceEventDto(Guid Id, string Code, string Name, GeofenceType PlaceType, GeofenceEventType EventType, DateTimeOffset DetectedAt, double Latitude, double Longitude, double Confidence);

public sealed record DwellDto(Guid Id, string? Place, DwellKind Kind, DateTimeOffset StartAt, DateTimeOffset? EndAt, int DurationMinutes, int ExpectedDurationMinutes, int ExcessDurationMinutes, DwellStatus Status);

public sealed record GapDto(Guid Id, DateTimeOffset GapStart, DateTimeOffset? GapEnd, int DurationMinutes, double LastKnownLatitude, double LastKnownLongitude, Severity Severity);

public sealed record TrackingHealthDto(
    TrackingHealth Health, int? AgeMinutes, TrackingSessionStatus? SessionStatus, string? DeviceId, string? DriverReference, int? BatteryPercentage, string? NetworkType, string? LocationPermission, string? AppVersion,
    DateTimeOffset? LastSeenAt, IReadOnlyList<GapDto> Gaps, int LocationCount, int SuspiciousCount, int LateCount);

/// <summary>What a completed trip looked like against the plan.</summary>
public sealed record JourneyAnalyticsDto(
    decimal? PlannedKm, double ActualKm, double? KmVariance, int? PlannedMinutes, int? ActualMinutes, int? MinutesVariance, int PlannedStops, int StopsReached, int UnplannedStops, int TotalDwellMinutes,
    int DeviationMinutes, int DeviationCount, IReadOnlyList<DwellDto> Dwells);

public sealed record DeviationReasonRequest(DelayReason Reason, string? Note);

public sealed record OverrideEtaRequest(DateTimeOffset Eta, string Reason);

public sealed record ManualMilestoneRequest(MilestoneType Type, Guid? StopId, DateTimeOffset? At, string Reason);

public sealed record SetDelayReasonRequest(DelayReason Reason, string? Note);

public sealed record AssignExceptionRequest(Guid? OwnerUserId, string? Department, DateTimeOffset? DueAt, Severity? Severity);

public sealed record EscalateExceptionRequest(string Reason, string? Level);

public sealed record ResolveExceptionRequest(string RootCause, DelayReason? DelayReason, string? ActionTaken);

public sealed record NoteRequest(string Text);

public sealed record ResolveAlertRequest(string? Note);

public sealed record ListShipmentsQuery(
    string? Search = null, Guid? TransporterId = null, string? Vehicle = null, string? Driver = null, string? Customer = null, string? Origin = null, string? Destination = null, string? Lane = null,
    ExecutionStatus? Execution = null, TrackingHealth? Tracking = null, RiskStatus? Risk = null, bool? HasException = null, bool? ActiveOnly = null, DateOnly? From = null, DateOnly? To = null,
    int Page = 1, int PageSize = 25);

public sealed record ListAlertsQuery(AlertStatus? Status = null, Severity? Severity = null, AlertType? Type = null, Guid? ShipmentId = null, int Page = 1, int PageSize = 25);

public sealed record ListExceptionsQuery(
    ExceptionStatus? Status = null, Severity? Severity = null, AlertType? Type = null, Guid? TransporterId = null, Guid? ShipmentId = null, bool? OpenOnly = null, bool? Overdue = null, int Page = 1, int PageSize = 25);

public sealed record LocationsQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, int? MaxPoints = null, bool? IncludeSuspicious = null, int Page = 1, int PageSize = 500);

public sealed record RecalculateEtaRequest(Guid? ShipmentId, string? TripReference);

public sealed record CreateLinkRequest(Guid ShipmentId, string? CustomerReference, string? CustomerName, int? ValidDays);

public sealed record CustomerLinkDto(Guid Id, Guid ShipmentId, string ShipmentReference, string? CustomerReference, string? CustomerName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, LinkStatus Status, DateTimeOffset? RevokedAt, int ViewCount, DateTimeOffset? LastViewedAt);

/// <summary>Returned once, when the link is made: the token is never stored and cannot be shown again.</summary>
public sealed record CreatedLinkDto(CustomerLinkDto Link, string Token, string Path);

/// <summary>What a customer may see. No prices, no internal exceptions, no notes, no ranking.</summary>
public sealed record CustomerTrackingDto(
    string ShipmentReference, string? Origin, string? Destination, string StatusLabel, IReadOnlyList<CustomerStepDto> Steps, double? Latitude, double? Longitude, DateTimeOffset? LocationAsOf, string? LocationLabel,
    DateTimeOffset? EtaAt, DateTimeOffset? DeliveryWindowStart, DateTimeOffset? DeliveryWindowEnd, string RiskLabel, bool Delivered, DateTimeOffset? DeliveredAt, IReadOnlyList<double[]> Route, DateTimeOffset AsOf);

public sealed record CustomerStepDto(string Label, string State, DateTimeOffset? At);
