using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Application.Engine;

public sealed record LocationPoint(
    string ClientLocationId, double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKph, double? Heading, DateTimeOffset CapturedAtUtc, bool MockLocation = false);

public sealed record LocationBatch(
    string TripReference, string DeviceId, IReadOnlyList<LocationPoint> Locations, string? AppVersion = null, int? BatteryPercentage = null, string? NetworkType = null,
    DateTimeOffset? SentAtUtc = null, string? LocationPermission = null, TrackingSource Source = TrackingSource.MobileApp);

/// <param name="Status">Accepted, Suspicious (kept but not used), Late (older than what is already known: history only) or Duplicate (already stored).</param>
public sealed record ProcessedLocation(string ClientLocationId, string Status, string? Note);

public sealed record RejectedLocation(string ClientLocationId, string Reason);

public sealed record BatchResult(IReadOnlyList<ProcessedLocation> Processed, IReadOnlyList<RejectedLocation> Rejected, int Accepted, int Duplicates, int Suspicious, int Late);

/// <summary>
/// Any source of locations hands its fixes to the same pipeline, so the geofence, route, dwell and arrival logic exists once. The phone is the first source; a GPS device, a
/// telematics feed or a carrier's API is another class like it.
/// </summary>
public interface ITrackingLocationProvider
{
    TrackingSource Source { get; }

    Task<Result<BatchResult>> ProcessLocationsAsync(LocationBatch batch, CancellationToken cancellationToken);
}

internal sealed class MobileTrackingLocationProvider(LocationPipeline pipeline) : ITrackingLocationProvider
{
    public TrackingSource Source => TrackingSource.MobileApp;

    public Task<Result<BatchResult>> ProcessLocationsAsync(LocationBatch batch, CancellationToken cancellationToken) =>
        pipeline.ProcessAsync(batch with { Source = TrackingSource.MobileApp }, cancellationToken);
}

/// <summary>
/// The path every location takes: validate, de-duplicate, keep, place the vehicle on its route, check geofences, dwell and deviation, then estimate arrival and judge the risk. The
/// whole batch is saved at once, with the events it raised, or not at all.
/// </summary>
internal sealed class LocationPipeline(
    TrackingDbContext db, TrackingAccess access, ITrackingContextLoader loader, IGeofenceService geofences, IMilestoneDetectionService milestones, IRouteDeviationService deviations,
    IDwellDetectionService dwell, IShipmentEtaService eta, ITrackingAlertService alerts, ITrackingNotificationService notifications, ITrackingLiveNotifier live, PendingEvents pending, Timeline timeline,
    TimeProvider clock)
{
    public async Task<Result<BatchResult>> ProcessAsync(LocationBatch batch, CancellationToken cancellationToken)
    {
        if (!access.CanExecute)
        {
            return TrackingAccess.Forbidden;
        }

        if (batch.Locations.Count == 0 || batch.Locations.Count > 1000 || string.IsNullOrWhiteSpace(batch.DeviceId) || batch.Locations.Any(l => string.IsNullOrWhiteSpace(l.ClientLocationId)))
        {
            return Error.Validation("tracking.batch_invalid", "Send between 1 and 1000 locations, each with its own id, from a named device.");
        }

        var shipment = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.TripReference == batch.TripReference, cancellationToken);
        if (shipment is null || !access.CanSee(shipment))
        {
            return TrackingAccess.ShipmentNotFound;
        }

        var session = shipment.CurrentSessionId is { } sessionId ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken) : null;
        if (session is null || !session.IsOpen)
        {
            return Error.Conflict("tracking.not_started", "Tracking has not been started for this trip, or it has stopped.");
        }

        var now = clock.GetUtcNow();
        var context = await loader.LoadAsync(shipment, session, now, cancellationToken);
        var rules = context.Settings;

        var ids = batch.Locations.Select(l => l.ClientLocationId).Distinct().ToList();
        var known = (await db.Locations.AsNoTracking().Where(l => l.DeviceId == batch.DeviceId && l.TripReference == batch.TripReference && ids.Contains(l.ClientLocationReference))
            .Select(l => l.ClientLocationReference).ToListAsync(cancellationToken)).ToHashSet();

        var processed = new List<ProcessedLocation>();
        var rejected = new List<RejectedLocation>();
        var seen = new HashSet<string>();
        var skewed = batch.SentAtUtc is { } sent && Math.Abs((now - sent).TotalMinutes) > rules.Validation.ClockSkewMinutes;
        LocationInput? lastLive = null;
        int live_ = 0, suspicious = 0, late = 0, duplicates = 0, accepted = 0;

        foreach (var point in batch.Locations.OrderBy(l => l.CapturedAtUtc))
        {
            if (known.Contains(point.ClientLocationId) || !seen.Add(point.ClientLocationId))
            {
                duplicates++;
                processed.Add(new ProcessedLocation(point.ClientLocationId, "Duplicate", "Already received."));
                continue;
            }

            var fix = new LocationInput(point.Latitude, point.Longitude, point.AccuracyMeters, point.SpeedKph, point.Heading, point.CapturedAtUtc, point.MockLocation);
            var isLate = shipment.LastCapturedAt is { } lastCaptured && fix.CapturedAt <= lastCaptured;
            var previous = !isLate && shipment.LastCapturedAt is { } at && shipment.LastLatitude is { } plat && shipment.LastLongitude is { } plon
                ? new PreviousFix(new GeoPoint(plat, plon), at, shipment.IdenticalCount) : null;
            var verdict = LocationValidator.Validate(fix, previous, now, rules.Validation);
            if (skewed)
            {
                verdict = verdict with { Anomalies = verdict.Anomalies | LocationAnomaly.DeviceTimeMismatch };
            }

            if (verdict.Status == LocationValidation.Rejected)
            {
                rejected.Add(new RejectedLocation(point.ClientLocationId, string.Join(' ', verdict.Reasons)));
                continue;
            }

            db.Locations.Add(TrackingLocation.Create(context.TenantId, session, point.ClientLocationId, fix, now, batch.DeviceId, batch.Source, verdict, isLate, batch.AppVersion, batch.BatteryPercentage, batch.NetworkType));
            if (verdict.Status == LocationValidation.Suspicious)
            {
                suspicious++;
                processed.Add(new ProcessedLocation(point.ClientLocationId, "Suspicious", string.Join(' ', verdict.Reasons)));
                continue;
            }

            if (isLate)
            {
                late++;
                processed.Add(new ProcessedLocation(point.ClientLocationId, "Late", "Older than what is already known: kept as history."));
                continue;
            }

            accepted++;
            processed.Add(new ProcessedLocation(point.ClientLocationId, "Accepted", null));
            await AdvanceAsync(context, fix, now, cancellationToken);
            lastLive = fix;
            live_++;
        }

        if (lastLive is { } latest)
        {
            await SettleAsync(context, latest, batch, now, cancellationToken);
        }

        if (suspicious > 0)
        {
            await alerts.RaiseAsync(context, AlertType.GpsAnomaly, $"gps:{session.Id:N}", $"{suspicious} location(s) from {batch.DeviceId} looked wrong and were not used.", null, cancellationToken);
        }

        if (string.Equals(batch.LocationPermission, "Denied", StringComparison.OrdinalIgnoreCase))
        {
            await alerts.RaiseAsync(context, AlertType.GpsUnavailable, $"gps-denied:{session.Id:N}", $"The driver's device cannot read its location ({batch.DeviceId}).", null, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(session.DeviceId) && session.DeviceId != batch.DeviceId)
        {
            timeline.Add(shipment, "DeviceChanged", $"Tracking moved from device {session.DeviceId} to {batch.DeviceId}", now, EventSource.System, reason: "device-changed");
            session.ChangeDevice(batch.DeviceId);
        }

        var latestCaptured = batch.Locations.Max(l => l.CapturedAtUtc);
        session.Located(latestCaptured, now, accepted + suspicious + late);
        await UpsertDeviceAsync(context.TenantId, batch, session, now, cancellationToken);

        foreach (var pendingEvent in pending.Drain())
        {
            shipment.Publish(pendingEvent);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("tracking.busy", "Another upload for this trip was being processed. Send it again.");
        }
        catch (DbUpdateException) when (batch.Locations.Count > 0)
        {
            return Error.Conflict("tracking.busy", "Another upload for this trip was being processed. Send it again.");
        }

        await FlushAsync(context, cancellationToken);
        if (live_ > 0)
        {
            await PushPositionAsync(context, cancellationToken);
        }

        return new BatchResult(processed, rejected, accepted, duplicates, suspicious, late);
    }

    /// <summary>One accepted fix: where the vehicle is on its route, then everything that can follow from that.</summary>
    private async Task AdvanceAsync(TrackingContext context, LocationInput fix, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        context.Fix = fix;
        context.ReceivedAt = now;
        var health = context.Settings.Health;

        // Anything that stopped us hearing from the vehicle is over now.
        if (shipment.LastCapturedAt is { } last && (fix.CapturedAt - last).TotalMinutes > health.StaleAfterMinutes)
        {
            if (context.OpenGap is { } open)
            {
                open.Close(fix.CapturedAt);
                await alerts.ResolveAsync(context, $"tracking-stale:{open.Id:N}", "Locations are arriving again.", cancellationToken);
                await alerts.ResolveAsync(context, $"tracking-lost:{open.Id:N}", "Locations are arriving again.", cancellationToken);
                context.OpenGap = null;
            }
            else
            {
                var gap = TrackingGap.Open(shipment, last, shipment.LastLatitude ?? fix.Latitude, shipment.LastLongitude ?? fix.Longitude, GapSeverity((int)(fix.CapturedAt - last).TotalMinutes, health), (int)(fix.CapturedAt - last).TotalMinutes);
                gap.Close(fix.CapturedAt);
                db.Gaps.Add(gap);
            }

            timeline.Add(shipment, ShipmentEventTypes.TrackingResumed, $"Locations resumed after {(int)(fix.CapturedAt - last).TotalMinutes} min without one", fix.CapturedAt, EventSource.Gps, fix.Latitude, fix.Longitude);
        }

        double? along = null;
        double? off = null;
        if (context.Route is { IsUsable: true } route)
        {
            var match = route.Match(context.Point, shipment.AlongKm);
            along = match.AlongKm;
            off = match.OffRouteKm;
        }

        context.AlongKm = along;
        context.OffRouteKm = off;

        var travelled = 0.0;
        var identical = 0;
        if (shipment.LastLatitude is { } plat && shipment.LastLongitude is { } plon)
        {
            var moved = Geo.DistanceKm(new GeoPoint(plat, plon), context.Point);
            identical = moved < 0.0005 ? shipment.IdenticalCount + 1 : 0;
            travelled = moved < 0.01 ? 0 : moved; // under ten metres is the GPS wandering, not the vehicle
        }

        shipment.MoveTo(fix, now, travelled, along, off, null, null, identical);
        await geofences.EvaluateAsync(context, cancellationToken);
        await milestones.ProcessLocationAsync(context, cancellationToken);
        await deviations.EvaluateAsync(context, cancellationToken);
        await dwell.EvaluateAsync(context, cancellationToken);
    }

    /// <summary>After the last accepted fix: the vehicle's current position, health, estimate and risk.</summary>
    private async Task SettleAsync(TrackingContext context, LocationInput latest, LocationBatch batch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        var key = string.IsNullOrWhiteSpace(shipment.VehicleReference) ? shipment.TripReference : shipment.VehicleReference!;
        var position = await db.Positions.FirstOrDefaultAsync(p => p.VehicleReference == key, cancellationToken);
        if (position is null)
        {
            position = CurrentVehiclePosition.Create(context.TenantId, key);
            db.Positions.Add(position);
        }

        position.Update(shipment, context.Session, latest, now, HealthEvaluator.IsMoving(latest.SpeedKph, context.Settings.Health), batch.BatteryPercentage, batch.NetworkType);
        shipment.SetHealth(TrackingHealth.Healthy);
        context.Fix = latest;
        await eta.UpdateAsync(context, force: false, cancellationToken, asOf: latest.CapturedAt < now ? latest.CapturedAt : now);
    }

    private async Task UpsertDeviceAsync(Guid tenantId, LocationBatch batch, TrackingSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == batch.DeviceId, cancellationToken);
        if (device is null)
        {
            device = TrackingDevice.Create(tenantId, batch.DeviceId);
            db.Devices.Add(device);
        }

        device.Seen(session.DriverReference, batch.AppVersion, batch.BatteryPercentage, batch.NetworkType, batch.LocationPermission, now);
    }

    internal static Severity GapSeverity(int minutes, HealthSetting health) =>
        minutes > health.LostAfterMinutes ? Severity.High : minutes > health.StaleAfterMinutes ? Severity.Warning : Severity.Informational;

    private async Task FlushAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        foreach (var notification in context.Notifications)
        {
            try
            {
                await notifications.NotifyAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // a failed push must not undo saved work
            }
        }
    }

    private async Task PushPositionAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        var s = context.Shipment;
        try
        {
            await live.PushAsync(context.TenantId, "position", new
            {
                shipmentId = s.ShipmentId, tripReference = s.TripReference, vehicleReference = s.VehicleReference, latitude = s.LastLatitude, longitude = s.LastLongitude, speedKph = s.LastSpeedKph, heading = s.LastHeading,
                capturedAt = s.LastCapturedAt, tracking = s.Tracking.ToString(), risk = s.Risk.ToString(), execution = s.Execution.ToString(), eta = s.CurrentEtaAt, progressPct = s.ProgressPct,
            }, s.TransporterId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // as above
        }
    }
}
