using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Mobile;

public sealed record StartTrackingRequest(string TripReference, string DeviceId, string? DriverReference = null, string? VehicleReference = null, string? ClientKey = null, string? AppVersion = null);

public sealed record StopTrackingRequest(string TripReference, string? DeviceId = null, string? Reason = null, bool Completed = true, string? ClientKey = null);

public sealed record TrackingSessionDto(
    Guid SessionId, string Reference, string TripReference, string ShipmentReference, string? VehicleReference, string? DriverReference, string? DeviceId, TrackingSessionStatus Status, DateTimeOffset StartedAt,
    DateTimeOffset? StoppedAt, DateTimeOffset? LastLocationAt, int LocationCount, int IntervalSeconds, int StationaryIntervalSeconds, int ApproachingIntervalSeconds, double ApproachingKm, bool Adaptive,
    int StaleAfterMinutes, int LostAfterMinutes);

/// <summary>Starting and stopping tracking for a trip. Tracking exists only for the length of the trip, and a retried start or stop changes nothing.</summary>
internal sealed class SessionService(
    TrackingDbContext db, TrackingAccess access, TrackedShipmentFactory factory, ITrackingContextLoader loader, IGeofenceService geofences, IDwellDetectionService dwell, ITrackingAlertService alerts,
    ITrackingSettings settings, ISequenceGenerator sequences, RouteHistory history, Timeline timeline, PendingEvents pending, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<TrackingSessionDto>> StartAsync(StartTrackingRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanExecute || user.TenantId is not { } tenantId)
        {
            return TrackingAccess.Forbidden;
        }

        if (string.IsNullOrWhiteSpace(request.TripReference) || string.IsNullOrWhiteSpace(request.DeviceId))
        {
            return Error.Validation("tracking.start_invalid", "Say which trip and which device.");
        }

        if (Clean(request.ClientKey) is { } key && await Replay(key, "start", cancellationToken) is { } replay)
        {
            return replay;
        }

        var ensured = await factory.EnsureAsync(request.TripReference.Trim(), cancellationToken);
        if (ensured.IsFailure)
        {
            return ensured.Error;
        }

        var shipment = ensured.Value;
        if (!access.CanSee(shipment))
        {
            return TrackingAccess.ShipmentNotFound;
        }

        if (!shipment.IsActive)
        {
            return Error.Conflict("tracking.trip_finished", $"This trip is {shipment.Execution}: tracking cannot be started.");
        }

        var now = clock.GetUtcNow();
        var open = shipment.CurrentSessionId is { } current ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == current && s.Status != TrackingSessionStatus.Completed && s.Status != TrackingSessionStatus.Cancelled, cancellationToken) : null;
        TrackingSession session;
        if (open is not null)
        {
            // Already being tracked: the same device is a retry; another device takes over.
            session = open;
            if (open.Status == TrackingSessionStatus.Paused)
            {
                open.Resume();
            }

            if (open.DeviceId != request.DeviceId.Trim())
            {
                timeline.Add(shipment, "DeviceChanged", $"Tracking moved from device {open.DeviceId} to {request.DeviceId.Trim()}", now, EventSource.Driver, reason: "device-changed", by: user.UserId);
                open.ChangeDevice(request.DeviceId.Trim());
            }
        }
        else
        {
            var reference = $"TS-{await sequences.NextAsync(tenantId, "tracking_session", cancellationToken):D5}";
            session = TrackingSession.Start(tenantId, reference, shipment, Clean(request.DriverReference) ?? shipment.DriverName, Clean(request.VehicleReference), request.DeviceId.Trim(), user.UserId, now);
            db.Sessions.Add(session);
            shipment.Start(session.Id, now, Clean(request.VehicleReference), Clean(request.DriverReference));
            timeline.Add(shipment, ShipmentEventTypes.TrackingStarted, $"Tracking started on {session.VehicleReference ?? "the vehicle"}", now, EventSource.Driver, by: user.UserId);
            var assigned = await db.Milestones.FirstOrDefaultAsync(m => m.TrackedShipmentId == shipment.Id && m.Type == MilestoneType.VehicleAssigned, cancellationToken);
            assigned?.Achieve(now, EventSource.Driver);
        }

        foreach (var e in pending.Drain())
        {
            shipment.Publish(e);
        }

        var dto = await ToDtoAsync(session, cancellationToken);
        if (Clean(request.ClientKey) is { } clientKey)
        {
            db.SyncRecords.Add(TrackingSyncRecord.Create(tenantId, clientKey, "start", request.DeviceId.Trim(), shipment.TripReference, JsonSerializer.Serialize(dto, TrackingSettings.Json), now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return dto;
    }

    public async Task<Result<TrackingSessionDto>> StopAsync(StopTrackingRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanExecute || user.TenantId is not { } tenantId)
        {
            return TrackingAccess.Forbidden;
        }

        if (Clean(request.ClientKey) is { } key && await Replay(key, "stop", cancellationToken) is { } replay)
        {
            return replay;
        }

        var shipment = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.TripReference == request.TripReference, cancellationToken);
        if (shipment is null || !access.CanSee(shipment))
        {
            return TrackingAccess.ShipmentNotFound;
        }

        var session = shipment.CurrentSessionId is { } id ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken) : null;
        if (session is null || !session.IsOpen)
        {
            // Already stopped: a repeat of the same stop is not an error.
            var last = await db.Sessions.OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(s => s.TrackedShipmentId == shipment.Id, cancellationToken);
            return last is null ? Error.Conflict("tracking.not_started", "Tracking was never started for this trip.") : await ToDtoAsync(last, cancellationToken);
        }

        var reason = Clean(request.Reason) ?? (request.Completed ? "Trip completed" : "Stopped by the driver");
        await CloseAsync(shipment, session, reason, request.Completed, cancellationToken);

        var dto = await ToDtoAsync(session, cancellationToken);
        if (Clean(request.ClientKey) is { } clientKey)
        {
            db.SyncRecords.Add(TrackingSyncRecord.Create(tenantId, clientKey, "stop", request.DeviceId, shipment.TripReference, JsonSerializer.Serialize(dto, TrackingSettings.Json), clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
        return dto;
    }


    /// <summary>Ends a session and settles everything the trip was waiting on. Used by the driver stopping, and by the trip being delivered, which needs no one's permission.</summary>
    internal async Task CloseAsync(TrackedShipment shipment, TrackingSession session, string reason, bool completed, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var context = await loader.LoadAsync(shipment, session, now, cancellationToken);
        context.Fix = new LocationInput(shipment.LastLatitude ?? 0, shipment.LastLongitude ?? 0, null, null, null, shipment.LastCapturedAt ?? now);
        await geofences.FlushAsync(context, shipment.LastCapturedAt ?? now, cancellationToken);
        await dwell.CloseAsync(context, now, cancellationToken);
        if (context.OpenGap is { } gap)
        {
            gap.Close(now);
            context.OpenGap = null;
        }

        foreach (var open in context.Alerts.Where(a => a.Status != AlertStatus.Resolved && (a.DedupeKey.StartsWith("tracking-", StringComparison.Ordinal) || a.DedupeKey.StartsWith("gps", StringComparison.Ordinal))).ToList())
        {
            await alerts.ResolveAsync(context, open.DedupeKey, "Tracking has stopped.", cancellationToken);
        }

        session.Stop(now, reason, completed);
        if (completed)
        {
            // Kept after the raw points are purged, so the trip can still be replayed and its route shown.
            shipment.SetActualRoute(await history.BuildSummaryAsync(shipment.ShipmentId, cancellationToken));
        }

        shipment.StopTracking(now, reason, completed);
        if (completed)
        {
            var done = context.Milestones.FirstOrDefault(m => m.Type == MilestoneType.TrackingCompleted && m.Status != MilestoneStatus.Achieved);
            done?.Achieve(now, EventSource.Driver);
        }

        timeline.Add(shipment, completed ? ShipmentEventTypes.TrackingCompleted : ShipmentEventTypes.TrackingStopped, completed ? "Tracking completed" : $"Tracking stopped: {reason}", now, EventSource.Driver, by: user.UserId);
        var key2 = string.IsNullOrWhiteSpace(shipment.VehicleReference) ? shipment.TripReference : shipment.VehicleReference!;
        if (await db.Positions.FirstOrDefaultAsync(p => p.VehicleReference == key2, cancellationToken) is { } position)
        {
            position.SetHealth(TrackingHealth.Completed, now);
        }

        foreach (var e in pending.Drain())
        {
            shipment.Publish(e);
        }
    }

    /// <summary>The driver's own view of a trip they may track: its status and how often to report.</summary>
    public async Task<Result<TrackingSessionDto?>> CurrentAsync(string tripReference, CancellationToken cancellationToken)
    {
        if (!access.CanExecute)
        {
            return TrackingAccess.Forbidden;
        }

        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.TripReference == tripReference, cancellationToken);
        if (shipment is null || !access.CanSee(shipment))
        {
            return TrackingAccess.ShipmentNotFound;
        }

        var session = await db.Sessions.AsNoTracking().OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(s => s.TrackedShipmentId == shipment.Id, cancellationToken);
        return Result.Success<TrackingSessionDto?>(session is null ? null : await ToDtoAsync(session, cancellationToken));
    }

    public async Task<TrackingSessionDto> ToDtoAsync(TrackingSession s, CancellationToken cancellationToken)
    {
        var interval = await settings.GetAsync<IntervalSetting>(TrackingSettingKeys.Interval, cancellationToken);
        var health = await settings.GetAsync<HealthSetting>(TrackingSettingKeys.Health, cancellationToken);
        return new TrackingSessionDto(
            s.Id, s.Reference, s.TripReference, s.ShipmentReference, s.VehicleReference, s.DriverReference, s.DeviceId, s.Status, s.StartedAt, s.StoppedAt, s.LastLocationAt, s.LocationCount,
            interval.ActiveSeconds, interval.StationarySeconds, interval.ApproachingSeconds, interval.ApproachingKm, interval.Adaptive, health.StaleAfterMinutes, health.LostAfterMinutes);
    }

    private async Task<TrackingSessionDto?> Replay(string key, string operation, CancellationToken cancellationToken)
    {
        var record = await db.SyncRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Operation == operation && r.ClientKey == key, cancellationToken);
        return record?.ResultJson is { } json ? JsonSerializer.Deserialize<TrackingSessionDto>(json, TrackingSettings.Json) : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
