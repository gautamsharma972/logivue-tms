using Tms.Modules.Tracking.Domain;

namespace Tms.Modules.Tracking.Application.Queries;

internal static class TrackingMapper
{
    public static TrackedShipmentSummaryDto Summary(TrackedShipment s, int openExceptions, DateTimeOffset now, HealthSetting health) => new(
        s.Id, s.ShipmentId, s.ShipmentReference, s.TripReference, s.TransporterId, s.TransporterReference, s.VehicleReference, s.DriverName, s.DriverPhone, s.CustomerName, s.OriginName, s.DestinationName,
        s.Execution, s.Tracking, s.Risk, s.Delivery, s.PlannedArrivalAt, s.CurrentEtaAt, s.SystemEtaAt, s.EtaOverrideAt is not null, s.EtaConfidence, s.DelayMinutes, s.ProgressPct, s.RemainingKm,
        s.LastLatitude, s.LastLongitude, s.LastCapturedAt, s.LastSpeedKph, s.LastHeading, s.LastCapturedAt is { } at ? (int)Math.Max(0, (now - at).TotalMinutes) : null, openExceptions,
        s.OffRouteKm is not { } off || !s.DeviationOpen && off <= 3, HealthEvaluator.IsMoving(s.LastSpeedKph, health) && s.Tracking == TrackingHealth.Healthy, s.StartedAt, s.CompletedAt);

    public static StopDto Stop(ShipmentStop s) => new(
        s.Id, s.Sequence, s.Kind, s.Name, s.City, s.Latitude, s.Longitude, s.RadiusM, s.PlaceType, s.Reference, s.CustomerName, s.PlannedArrival, s.WindowStart, s.WindowEnd, s.ExpectedDwellMinutes, s.Status,
        s.ArrivedAt, s.DepartedAt, s.EtaAt, s.EtaConfidence, s.DelayMinutes, s.Risk, s.AlongKm);

    public static AlertDto Alert(TrackingAlert a, DateTimeOffset now) => new(
        a.Id, a.Type, a.Severity, a.Status, a.TrackedShipmentId, a.ShipmentReference, a.TripReference, a.VehicleReference, a.Message, a.RaisedAt, a.DueAt, a.Status != AlertStatus.Resolved && a.DueAt < now,
        a.AcknowledgedAt, a.ResolvedAt, a.ResolutionNote, a.ExceptionId);

    public static TrackingExceptionSummaryDto Exception(TrackingException e, DateTimeOffset now) => new(
        e.Id, e.Number, e.Type, e.Severity, e.Status, e.TrackedShipmentId, e.ShipmentReference, e.TripReference, e.VehicleReference, e.TransporterId, e.TransporterReference, e.Description, e.RaisedAt, e.DueAt,
        e.IsOpen && e.DueAt < now, e.OwnerUserId, e.Department, e.EscalationLevel, e.EscalatedTo, e.ConditionClearedAt is not null, Math.Round(((e.ResolvedAt ?? now) - e.RaisedAt).TotalMinutes, 0));

    public static GeofenceDto Geofence(Geofence g) => new(
        g.Id, g.Code, g.Name, g.Type, g.CenterLatitude, g.CenterLongitude, g.RadiusMeters, g.Polygon?.Select(p => new[] { p.Latitude, p.Longitude }).ToList(), g.Status, g.EffectiveFrom, g.EffectiveTo, g.Version);

    public static DwellDto Dwell(DwellEvent d) => new(d.Id, d.Place, d.Kind, d.StartAt, d.EndAt, d.DurationMinutes, d.ExpectedDurationMinutes, d.ExcessDurationMinutes, d.Status);
}
