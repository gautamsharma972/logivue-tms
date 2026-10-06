using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Application.Mobile;

public sealed record SingleLocationRequest(
    string TripReference, string DeviceId, LocationPoint Location, string? AppVersion = null, int? BatteryPercentage = null, string? NetworkType = null, string? LocationPermission = null);

public sealed record DeviceStatusRequest(
    string DeviceId, string? TripReference, string? LocationPermission, int? BatteryPercentage = null, string? NetworkType = null, string? AppVersion = null, string? Note = null);

public sealed record SyncCommand(string ClientKey, string Type, StartTrackingRequest? Start = null, StopTrackingRequest? Stop = null, LocationBatch? Locations = null, DeviceStatusRequest? Status = null);

public sealed record SyncRequest(string DeviceId, IReadOnlyList<SyncCommand> Commands);

public sealed record SyncCommandResult(string ClientKey, string Type, bool Ok, string? Code, string? Error, object? Result);

public sealed record MobileTripDto(
    Guid ShipmentId, string TripReference, string ShipmentReference, string? VehicleReference, string? DriverName, string? Origin, string? Destination, ExecutionStatus Execution, RiskStatus Risk, DateTimeOffset? EtaAt,
    DateTimeOffset? PlannedStartAt, IReadOnlyList<MobileStopDto> Stops, TrackingSessionDto? Session, bool CanStart);

public sealed record MobileStopDto(int Sequence, StopKind Kind, string Name, string? City, DateTimeOffset? PlannedArrival, StopStatus Status, double? Latitude, double? Longitude);

/// <summary>
/// What the driver's phone talks to: the trips it may track, starting and stopping, sending locations one at a time or in a batch, and a sync call that replays everything the phone
/// saved while it had no signal. Every command carries a key the phone chose, so sending it twice does the same as sending it once.
/// </summary>
internal sealed class MobileTrackingHandler(
    TrackingDbContext db, TrackingAccess access, SessionService sessions, ITrackingLocationProvider locations, ITrackingContextLoader loader, ITrackingAlertService alerts, Timeline timeline,
    TimeProvider clock)
{
    public Task<Result<TrackingSessionDto>> StartAsync(StartTrackingRequest request, CancellationToken cancellationToken) => sessions.StartAsync(request, cancellationToken);

    public Task<Result<TrackingSessionDto>> StopAsync(StopTrackingRequest request, CancellationToken cancellationToken) => sessions.StopAsync(request, cancellationToken);

    public Task<Result<BatchResult>> BatchAsync(LocationBatch batch, CancellationToken cancellationToken) => locations.ProcessLocationsAsync(batch, cancellationToken);

    public Task<Result<BatchResult>> LocationAsync(SingleLocationRequest request, CancellationToken cancellationToken) =>
        locations.ProcessLocationsAsync(new LocationBatch(request.TripReference, request.DeviceId, [request.Location], request.AppVersion, request.BatteryPercentage, request.NetworkType, null, request.LocationPermission), cancellationToken);

    /// <summary>The trips a driver (a vendor-portal user) can track now: their company's active trips, with the state of tracking on each.</summary>
    public async Task<Result<IReadOnlyList<MobileTripDto>>> TripsAsync(CancellationToken cancellationToken)
    {
        if (!access.CanExecute)
        {
            return TrackingAccess.Forbidden;
        }

        var rows = db.Shipments.AsNoTracking().Include(s => s.Stops).Where(s => s.Execution != ExecutionStatus.Completed && s.Execution != ExecutionStatus.Cancelled && s.Execution != ExecutionStatus.Delivered);
        if (access.IsVendor)
        {
            rows = rows.Where(s => s.TransporterId == access.VendorTransporterId);
        }

        var trips = await rows.OrderBy(s => s.PlannedStartAt).Take(100).ToListAsync(cancellationToken);
        var sessionIds = trips.Where(t => t.CurrentSessionId != null).Select(t => t.CurrentSessionId!.Value).ToList();
        var open = await db.Sessions.AsNoTracking().Where(s => sessionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var result = new List<MobileTripDto>();
        foreach (var trip in trips)
        {
            TrackingSessionDto? session = trip.CurrentSessionId is { } id && open.TryGetValue(id, out var s) ? await sessions.ToDtoAsync(s, cancellationToken) : null;
            result.Add(new MobileTripDto(
                trip.ShipmentId, trip.TripReference, trip.ShipmentReference, trip.VehicleReference, trip.DriverName, trip.OriginName, trip.DestinationName, trip.Execution, trip.Risk, trip.CurrentEtaAt, trip.PlannedStartAt,
                trip.Stops.OrderBy(x => x.Sequence).Select(x => new MobileStopDto(x.Sequence, x.Kind, x.Name, x.City, x.PlannedArrival, x.Status, x.Latitude, x.Longitude)).ToList(), session, session is null));
        }

        return result;
    }

    public Task<Result<TrackingSessionDto?>> CurrentAsync(string tripReference, CancellationToken cancellationToken) => sessions.CurrentAsync(tripReference, cancellationToken);

    /// <summary>The device says something about itself: it lost permission to read the location, or got it back. Nothing else tells the control tower why locations stopped.</summary>
    public async Task<Result> StatusAsync(DeviceStatusRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanExecute)
        {
            return TrackingAccess.Forbidden;
        }

        var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId, cancellationToken);
        var now = clock.GetUtcNow();
        if (device is null)
        {
            device = TrackingDevice.Create(access.TenantId ?? throw new InvalidOperationException("No tenant."), request.DeviceId);
            db.Devices.Add(device);
        }

        device.Seen(null, request.AppVersion, request.BatteryPercentage, request.NetworkType, request.LocationPermission, now);

        if (!string.IsNullOrWhiteSpace(request.TripReference))
        {
            var shipment = await db.Shipments.Include(s => s.Stops).FirstOrDefaultAsync(s => s.TripReference == request.TripReference, cancellationToken);
            if (shipment is null || !access.CanSee(shipment))
            {
                return TrackingAccess.ShipmentNotFound;
            }

            var session = shipment.CurrentSessionId is { } sid ? await db.Sessions.FirstOrDefaultAsync(s => s.Id == sid, cancellationToken) : null;
            if (session is not null)
            {
                var context = await loader.LoadAsync(shipment, session, now, cancellationToken);
                if (string.Equals(request.LocationPermission, "Denied", StringComparison.OrdinalIgnoreCase))
                {
                    await alerts.RaiseAsync(context, AlertType.GpsUnavailable, $"gps-denied:{session.Id:N}", $"The driver's device cannot read its location{(string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $": {request.Note.Trim()}")}.", null, cancellationToken);
                    timeline.Add(shipment, "GpsUnavailable", "The driver's device cannot read its location", now, EventSource.Driver);
                }
                else if (string.Equals(request.LocationPermission, "Granted", StringComparison.OrdinalIgnoreCase))
                {
                    await alerts.ResolveAsync(context, $"gps-denied:{session.Id:N}", "The device can read its location again.", cancellationToken);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Replays what the phone saved while it had no signal, in order. Each command is judged on its own: one that fails does not stop the ones after it, and the phone is told which
    /// worked, so it can keep what did not.
    /// </summary>
    public async Task<Result<IReadOnlyList<SyncCommandResult>>> SyncAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanExecute)
        {
            return TrackingAccess.Forbidden;
        }

        if (request.Commands.Count is 0 or > 200)
        {
            return Error.Validation("tracking.sync_invalid", "Send between 1 and 200 commands.");
        }

        var results = new List<SyncCommandResult>();
        foreach (var command in request.Commands)
        {
            object? payload = null;
            Error? error = null;
            switch (command.Type.ToLowerInvariant())
            {
                case "start" when command.Start is { } start:
                {
                    var r = await sessions.StartAsync(start with { DeviceId = start.DeviceId, ClientKey = command.ClientKey }, cancellationToken);
                    (payload, error) = (r.IsSuccess ? r.Value : null, r.IsFailure ? r.Error : null);
                    break;
                }

                case "stop" when command.Stop is { } stop:
                {
                    var r = await sessions.StopAsync(stop with { ClientKey = command.ClientKey }, cancellationToken);
                    (payload, error) = (r.IsSuccess ? r.Value : null, r.IsFailure ? r.Error : null);
                    break;
                }

                case "locations" when command.Locations is { } batch:
                {
                    var r = await locations.ProcessLocationsAsync(batch, cancellationToken);
                    (payload, error) = (r.IsSuccess ? r.Value : null, r.IsFailure ? r.Error : null);
                    break;
                }

                case "status" when command.Status is { } status:
                {
                    var r = await StatusAsync(status, cancellationToken);
                    error = r.IsFailure ? r.Error : null;
                    break;
                }

                default:
                    error = Error.Validation("tracking.sync_command", $"'{command.Type}' is not a command this can run.");
                    break;
            }

            results.Add(new SyncCommandResult(command.ClientKey, command.Type, error is null, error?.Code, error?.Description, payload));
        }

        return results;
    }
}
