using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Application.Queries;

/// <summary>Reads about one trip or a list of trips: where it is, how it is doing, what happened, what is expected. Everything here reads the read model, not the GPS history.</summary>
internal sealed class ShipmentQueryHandler(TrackingDbContext db, TrackingAccess access, TrackingHealthMonitor monitor, ITrackingSettings settings, RouteHistory history, TimeProvider clock)
{
    private IQueryable<TrackedShipment> Scoped()
    {
        var rows = db.Shipments.AsNoTracking().AsQueryable();
        return access.IsVendor ? rows.Where(s => s.TransporterId == access.VendorTransporterId) : rows;
    }

    private bool Allowed => access.IsVendor ? access.CanExecute : access.CanRead;

    public async Task<Result<PagedResult<TrackedShipmentSummaryDto>>> ListAsync(ListShipmentsQuery query, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return TrackingAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var rows = Scoped();
        if (!access.IsVendor && query.TransporterId is { } transporter)
        {
            rows = rows.Where(s => s.TransporterId == transporter);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(s => s.ShipmentReference.Contains(term) || s.TripReference.Contains(term) || (s.VehicleReference != null && s.VehicleReference.Contains(term))
                || (s.DriverName != null && s.DriverName.Contains(term)) || (s.CustomerName != null && s.CustomerName.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Vehicle))
        {
            var v = query.Vehicle.Trim();
            rows = rows.Where(s => s.VehicleReference != null && s.VehicleReference.Contains(v));
        }

        if (!string.IsNullOrWhiteSpace(query.Driver))
        {
            var d = query.Driver.Trim();
            rows = rows.Where(s => s.DriverName != null && s.DriverName.Contains(d));
        }

        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var c = query.Customer.Trim();
            rows = rows.Where(s => s.CustomerName != null && s.CustomerName.Contains(c));
        }

        if (!string.IsNullOrWhiteSpace(query.Origin))
        {
            var o = query.Origin.Trim();
            rows = rows.Where(s => s.OriginName != null && s.OriginName.Contains(o));
        }

        if (!string.IsNullOrWhiteSpace(query.Destination))
        {
            var d = query.Destination.Trim();
            rows = rows.Where(s => s.DestinationName != null && s.DestinationName.Contains(d));
        }

        if (!string.IsNullOrWhiteSpace(query.Lane))
        {
            var l = query.Lane.Trim();
            rows = rows.Where(s => (s.OriginName != null && s.OriginName.Contains(l)) || (s.DestinationName != null && s.DestinationName.Contains(l)));
        }

        if (query.Execution is { } execution)
        {
            rows = rows.Where(s => s.Execution == execution);
        }

        if (query.Tracking is { } tracking)
        {
            rows = rows.Where(s => s.Tracking == tracking);
        }

        if (query.Risk is { } risk)
        {
            rows = rows.Where(s => s.Risk == risk);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(s => s.Execution != ExecutionStatus.Completed && s.Execution != ExecutionStatus.Cancelled && s.Execution != ExecutionStatus.Delivered);
        }

        if (query.HasException is { } hasException)
        {
            rows = hasException
                ? rows.Where(s => db.Exceptions.Any(e => e.TrackedShipmentId == s.Id && (e.Status == ExceptionStatus.Open || e.Status == ExceptionStatus.Acknowledged || e.Status == ExceptionStatus.InProgress || e.Status == ExceptionStatus.Escalated)))
                : rows.Where(s => !db.Exceptions.Any(e => e.TrackedShipmentId == s.Id && (e.Status == ExceptionStatus.Open || e.Status == ExceptionStatus.Acknowledged || e.Status == ExceptionStatus.InProgress || e.Status == ExceptionStatus.Escalated)));
        }

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Clock.India);
            rows = rows.Where(s => (s.StartedAt ?? s.PlannedStartAt ?? s.CreatedAt) >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Clock.India);
            rows = rows.Where(s => (s.StartedAt ?? s.PlannedStartAt ?? s.CreatedAt) < end);
        }

        // The ones that need attention come first: the worst risk, then tracking that has gone quiet, then the soonest arrival.
        var ordered = rows
            .OrderByDescending(s => s.Risk == RiskStatus.SeverelyDelayed ? 5 : s.Risk == RiskStatus.Delayed ? 4 : s.Tracking == TrackingHealth.Lost ? 3 : s.Risk == RiskStatus.AtRisk ? 2 : s.Tracking == TrackingHealth.Stale ? 1 : 0)
            .ThenBy(s => s.SystemEtaAt ?? s.PlannedArrivalAt)
            .ThenBy(s => s.ShipmentReference);
        var page = await ordered.ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var items = page.Items.ToList();
        var health = await settings.GetAsync<HealthSetting>(TrackingSettingKeys.Health, cancellationToken);
        var open = await OpenExceptionCountsAsync(items.Select(s => s.Id).ToList(), cancellationToken);
        var now = clock.GetUtcNow();
        return new PagedResult<TrackedShipmentSummaryDto>(items.Select(s => TrackingMapper.Summary(s, open.GetValueOrDefault(s.Id), now, health)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<TrackedShipmentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var shipment = await FindAsync(id, includeStops: true, cancellationToken);
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        var s = shipment.Value;
        var health = await settings.GetAsync<HealthSetting>(TrackingSettingKeys.Health, cancellationToken);
        var open = (await OpenExceptionCountsAsync([s.Id], cancellationToken)).GetValueOrDefault(s.Id);
        var session = s.CurrentSessionId is { } sid ? await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid, cancellationToken)
            : await db.Sessions.AsNoTracking().Where(x => x.TrackedShipmentId == s.Id).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);
        var device = session?.DeviceId is { } deviceId ? await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken) : null;
        return new TrackedShipmentDto(
            TrackingMapper.Summary(s, open, clock.GetUtcNow(), health), s.Stops.OrderBy(x => x.Sequence).Select(TrackingMapper.Stop).ToList(), s.PlannedDistanceKm, s.PlannedDurationMinutes, Math.Round(s.TravelledKm, 1), s.OffRouteKm,
            s.RouteSource, s.PlannedStartAt, s.EtaOverrideAt, s.EtaOverrideReason, s.DelayReason, s.DelayNote, s.CurrentSessionId, session?.Status, session?.DeviceId, device?.LastBatteryPercentage, device?.LastNetworkType,
            device?.LocationPermission, s.Version);
    }

    public async Task<Result<CurrentLocationDto>> CurrentLocationAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var s = found.Value;
        if (s.LastLatitude is not { } lat || s.LastLongitude is not { } lon || s.LastCapturedAt is not { } at)
        {
            return Error.NotFound("tracking.no_location", "No location has been received for this trip yet.");
        }

        var now = clock.GetUtcNow();
        var key = string.IsNullOrWhiteSpace(s.VehicleReference) ? s.TripReference : s.VehicleReference!;
        var position = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.VehicleReference == key, cancellationToken);
        var health = await settings.GetAsync<HealthSetting>(TrackingSettingKeys.Health, cancellationToken);
        return new CurrentLocationDto(
            s.VehicleReference, s.TripReference, s.ShipmentReference, lat, lon, s.LastAccuracyM, s.LastSpeedKph, s.LastHeading, at, s.LastReceivedAt ?? at, s.Tracking, HealthEvaluator.IsMoving(s.LastSpeedKph, health) && s.Tracking == TrackingHealth.Healthy,
            (int)Math.Max(0, (now - at).TotalMinutes), position?.BatteryPercentage, position?.NetworkType, s.DriverName, s.TransporterReference);
    }

    public async Task<Result<IReadOnlyList<TimelineEntryDto>>> TimelineAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;
        var events = await db.Events.AsNoTracking().Where(e => e.TrackedShipmentId == shipment.Id).OrderBy(e => e.EventTime).ToListAsync(cancellationToken);
        var milestones = await db.Milestones.AsNoTracking().Where(m => m.TrackedShipmentId == shipment.Id).OrderBy(m => m.Order).ToListAsync(cancellationToken);
        var entries = new List<TimelineEntryDto>();
        entries.AddRange(events.Select(e => new TimelineEntryDto(e.EventTime, "Actual", e.Description, null, e.Source.ToString(), e.EventType, e.Latitude, e.Longitude, e.StopId, e.ReasonCode)));
        foreach (var m in milestones)
        {
            if (m.PlannedAt is { } planned)
            {
                entries.Add(new TimelineEntryDto(planned, "Planned", m.Label, null, "Planning", m.Type.ToString(), null, null, m.StopId, null));
            }

            if (m.Status != MilestoneStatus.Achieved && m.EstimatedAt is { } estimated)
            {
                entries.Add(new TimelineEntryDto(estimated, "Estimated", m.Label, null, "System", m.Type.ToString(), null, null, m.StopId, null));
            }
        }

        return entries.OrderBy(e => e.At).ThenBy(e => e.Kind == "Planned" ? 0 : e.Kind == "Actual" ? 1 : 2).ToList();
    }

    public async Task<Result<EtaDto>> EtaAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var s = found.Value;
        var history = await db.EtaPredictions.AsNoTracking().Where(p => p.TrackedShipmentId == s.Id && p.IsFinalDestination).OrderByDescending(p => p.PredictedAt).Take(100)
            .Select(p => new EtaHistoryDto(p.PredictedAt, p.PredictedEta, p.RemainingKm, p.Confidence, p.RiskLevel, p.DelayMinutes, p.IsFinalDestination, p.StopId)).ToListAsync(cancellationToken);
        var stops = s.Stops.OrderBy(x => x.Sequence).Select(x => new EtaStopDto(x.Id, x.Name, x.Kind, x.WindowEnd ?? x.PlannedArrival, x.EtaAt, x.EtaConfidence, x.DelayMinutes, x.Risk, EtaCalculator.LevelOf(x.Risk), x.Status)).ToList();
        return new EtaDto(
            s.PlannedArrivalAt, s.SystemEtaAt, s.CurrentEtaAt, s.EtaOverrideAt is not null, s.EtaOverrideAt, s.EtaOverrideReason, s.EtaConfidence, s.Risk, EtaCalculator.LevelOf(s.Risk), s.DelayMinutes, "rules-1", stops, history);
    }

    public async Task<Result<RouteDto>> RouteAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var s = found.Value;
        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.TrackedShipmentId == s.Id, cancellationToken);
        var deviations = await db.Deviations.AsNoTracking().Where(d => d.TrackedShipmentId == s.Id).OrderBy(d => d.DetectedAt)
            .Select(d => new RouteDeviationDto(d.Id, d.DetectedAt, d.Latitude, d.Longitude, d.DistanceFromRouteKm, d.DurationMinutes, d.Severity, d.Status, d.Reason, d.ReasonNote, d.ResolvedAt)).ToListAsync(cancellationToken);
        return new RouteDto(
            route?.Points.Select(p => new[] { p.Latitude, p.Longitude }).ToList() ?? [], route?.LengthKm ?? 0, route?.Source ?? s.RouteSource, s.PlannedDistanceKm, s.PlannedDurationMinutes, Math.Round(s.TravelledKm, 1), s.RemainingKm, s.ProgressPct,
            s.Stops.OrderBy(x => x.Sequence).Select(TrackingMapper.Stop).ToList(), deviations);
    }

    /// <summary>The trail the vehicle left. For replay a point count can be asked for and the trail is thinned evenly to it; the first and last points are always kept.</summary>
    /// <summary>Where the vehicle went, in time order, for the replay player: the raw points while they exist, the saved simplified path after they have been purged.</summary>
    public async Task<Result<ReplayDto>> ReplayAsync(Guid id, DateTimeOffset? from, DateTimeOffset? to, int? maxPoints, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: false, cancellationToken);
        return found.IsFailure ? found.Error : await history.ReplayAsync(found.Value, from, to, maxPoints ?? 1500, cancellationToken);
    }

    public async Task<Result<PagedResult<LocationDto>>> LocationsAsync(Guid id, LocationsQuery query, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var rows = db.Locations.AsNoTracking().Where(l => l.ShipmentId == found.Value.ShipmentId);
        if (query.IncludeSuspicious != true)
        {
            rows = rows.Where(l => l.Validation == LocationValidation.Valid);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(l => l.CapturedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(l => l.CapturedAt <= to);
        }

        var ordered = rows.OrderBy(l => l.CapturedAt);
        if (query.MaxPoints is { } max and > 1)
        {
            var all = await ordered.Select(l => new LocationDto(l.Id, l.Latitude, l.Longitude, l.AccuracyMeters, l.SpeedKph, l.Heading, l.CapturedAt, l.ReceivedAt, l.Validation, l.Anomalies, l.Reasons, l.IsLate, l.DeviceId, l.Source))
                .Take(200_000).ToListAsync(cancellationToken);
            var thinned = all.Count <= max ? all : Enumerable.Range(0, max).Select(i => all[(int)Math.Round(i * (all.Count - 1.0) / (max - 1))]).ToList();
            return new PagedResult<LocationDto>(thinned, 1, thinned.Count, all.Count);
        }

        return await ordered.Select(l => new LocationDto(l.Id, l.Latitude, l.Longitude, l.AccuracyMeters, l.SpeedKph, l.Heading, l.CapturedAt, l.ReceivedAt, l.Validation, l.Anomalies, l.Reasons, l.IsLate, l.DeviceId, l.Source))
            .ToPagedAsync(query.Page, Math.Clamp(query.PageSize, 1, 2000), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<TrackingExceptionSummaryDto>>> ExceptionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var now = clock.GetUtcNow();
        var rows = await db.Exceptions.AsNoTracking().Where(e => e.TrackedShipmentId == found.Value.Id).OrderByDescending(e => e.RaisedAt).ToListAsync(cancellationToken);
        return rows.Select(e => TrackingMapper.Exception(e, now)).ToList();
    }

    public async Task<Result<TrackingHealthDto>> HealthAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var s = found.Value;
        var session = await db.Sessions.AsNoTracking().Where(x => x.TrackedShipmentId == s.Id).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);
        var device = session?.DeviceId is { } deviceId ? await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken) : null;
        var gaps = await db.Gaps.AsNoTracking().Where(g => g.TrackedShipmentId == s.Id).OrderByDescending(g => g.GapStart).Take(50)
            .Select(g => new GapDto(g.Id, g.GapStart, g.GapEnd, g.DurationMinutes, g.LastKnownLatitude, g.LastKnownLongitude, g.Severity)).ToListAsync(cancellationToken);
        var counts = await db.Locations.AsNoTracking().Where(l => l.ShipmentId == s.ShipmentId).GroupBy(l => 1).Select(g => new
        {
            Total = g.Count(), Suspicious = g.Count(l => l.Validation == LocationValidation.Suspicious), Late = g.Count(l => l.IsLate),
        }).FirstOrDefaultAsync(cancellationToken);
        var age = s.LastCapturedAt is { } at ? (int?)Math.Max(0, (int)(clock.GetUtcNow() - at).TotalMinutes) : null;
        return new TrackingHealthDto(s.Tracking, age, session?.Status, session?.DeviceId, session?.DriverReference, device?.LastBatteryPercentage, device?.LastNetworkType, device?.LocationPermission, device?.AppVersion,
            device?.LastSeenAt, gaps, counts?.Total ?? 0, counts?.Suspicious ?? 0, counts?.Late ?? 0);
    }

    /// <summary>A finished (or running) trip against its plan: distance, time, stops, dwell and deviation.</summary>
    public async Task<Result<JourneyAnalyticsDto>> AnalyticsAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, includeStops: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var s = found.Value;
        var dwells = await db.Dwells.AsNoTracking().Where(d => d.TrackedShipmentId == s.Id).OrderBy(d => d.StartAt).ToListAsync(cancellationToken);
        var deviations = await db.Deviations.AsNoTracking().Where(d => d.TrackedShipmentId == s.Id).ToListAsync(cancellationToken);
        var end = s.CompletedAt ?? s.LastCapturedAt;
        int? actualMinutes = s.StartedAt is { } started && end is { } finished ? (int)(finished - started).TotalMinutes : null;
        var actualKm = Math.Round(s.TravelledKm, 1);
        return new JourneyAnalyticsDto(
            s.PlannedDistanceKm, actualKm, s.PlannedDistanceKm is { } planned ? Math.Round(actualKm - (double)planned, 1) : null, s.PlannedDurationMinutes, actualMinutes,
            s.PlannedDurationMinutes is { } pm && actualMinutes is { } am ? am - pm : null, s.Stops.Count, s.Stops.Count(x => x.Status is StopStatus.Arrived or StopStatus.Departed),
            dwells.Count(d => d.Kind == DwellKind.UnplannedStop), dwells.Sum(d => d.DurationMinutes), deviations.Sum(d => d.DurationMinutes), deviations.Count, dwells.Select(TrackingMapper.Dwell).ToList());
    }

    internal async Task<Result<TrackedShipment>> FindAsync(Guid id, bool includeStops, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return TrackingAccess.Forbidden;
        }

        var query = Scoped();
        if (includeStops)
        {
            query = query.Include(s => s.Stops);
        }

        var shipment = await query.FirstOrDefaultAsync(s => s.Id == id || s.ShipmentId == id, cancellationToken);
        return shipment is null ? TrackingAccess.ShipmentNotFound : shipment;
    }

    private async Task<Dictionary<Guid, int>> OpenExceptionCountsAsync(List<Guid> shipmentIds, CancellationToken cancellationToken) =>
        shipmentIds.Count == 0
            ? []
            : await db.Exceptions.AsNoTracking().Where(e => shipmentIds.Contains(e.TrackedShipmentId) && (e.Status == ExceptionStatus.Open || e.Status == ExceptionStatus.Acknowledged || e.Status == ExceptionStatus.InProgress || e.Status == ExceptionStatus.Escalated))
                .GroupBy(e => e.TrackedShipmentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
}
