using System.Globalization;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Tracking.Application.Engine;

/// <summary>Writes to the trip's timeline.</summary>
internal sealed class Timeline(TrackingDbContext db)
{
    public ShipmentEvent Add(
        TrackedShipment shipment, string type, string description, DateTimeOffset at, EventSource source, double? lat = null, double? lon = null, string? geofence = null, Guid? stopId = null,
        string? reason = null, double? confidence = null, Guid? by = null)
    {
        var entry = ShipmentEvent.Create(shipment, type, description, at, source, lat, lon, geofence, stopId, reason, confidence, by);
        db.Events.Add(entry);
        return entry;
    }

    public static string Time(DateTimeOffset at) => at.ToOffset(Clock.India).ToString("HH:mm", CultureInfo.InvariantCulture);
}

internal interface IMilestoneDetectionService
{
    /// <summary>Moves the trip through its milestones as far as this fix shows: under way, approaching the next drop.</summary>
    Task ProcessLocationAsync(TrackingContext context, CancellationToken cancellationToken);

    void Achieve(TrackingContext context, MilestoneType type, Guid? stopId, DateTimeOffset at, EventSource source, string? reason = null);
}

internal sealed class MilestoneService(Timeline timeline) : IMilestoneDetectionService
{
    public static IReadOnlyList<Milestone> CreateFor(TrackedShipment shipment, MilestoneSetting settings, DateTimeOffset now)
    {
        var list = new List<Milestone>();
        var order = 0;
        void Add(MilestoneType type, string label, Guid? stopId, DateTimeOffset? planned)
        {
            if (settings.Has(type))
            {
                list.Add(Milestone.Create(shipment, type, label, order++, stopId, planned));
            }
        }

        Add(MilestoneType.VehicleAssigned, "Vehicle assigned", null, null);
        var pickups = shipment.Stops.Where(s => s.Kind == StopKind.Pickup).OrderBy(s => s.Sequence).ToList();
        var drops = shipment.Stops.Where(s => s.Kind == StopKind.Drop).OrderBy(s => s.Sequence).ToList();
        for (var i = 0; i < pickups.Count; i++)
        {
            Add(i == 0 ? MilestoneType.ArrivedOrigin : MilestoneType.ArrivedStop, i == 0 ? "Arrived at origin" : $"Arrived at pickup {pickups[i].Name}", pickups[i].Id, pickups[i].PlannedArrival);
            Add(i == 0 ? MilestoneType.DepartedOrigin : MilestoneType.DepartedStop, i == 0 ? "Departed origin" : $"Left pickup {pickups[i].Name}", pickups[i].Id, null);
        }

        Add(MilestoneType.InTransit, "In transit", null, null);
        for (var i = 0; i < drops.Count; i++)
        {
            var last = i == drops.Count - 1;
            Add(last ? MilestoneType.ApproachingDestination : MilestoneType.ApproachingStop, last ? "Approaching destination" : $"Approaching {drops[i].Name}", drops[i].Id, null);
            Add(last ? MilestoneType.ArrivedDestination : MilestoneType.ArrivedStop, last ? $"Arrived at {drops[i].Name}" : $"Arrived at {drops[i].Name}", drops[i].Id, drops[i].WindowEnd ?? drops[i].PlannedArrival);
            if (!last)
            {
                Add(MilestoneType.DepartedStop, $"Left {drops[i].Name}", drops[i].Id, null);
            }

            Add(MilestoneType.Delivered, $"Delivered at {drops[i].Name}", drops[i].Id, drops[i].WindowEnd ?? drops[i].PlannedArrival);
        }

        Add(MilestoneType.TrackingCompleted, "Tracking completed", null, null);
        return list;
    }

    public void Achieve(TrackingContext context, MilestoneType type, Guid? stopId, DateTimeOffset at, EventSource source, string? reason = null)
    {
        var milestone = context.Milestones.FirstOrDefault(m => m.Type == type && m.Status != MilestoneStatus.Achieved && (stopId is null || m.StopId == stopId));
        milestone?.Achieve(at, source, reason);
    }

    public Task ProcessLocationAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        var at = context.Fix.CapturedAt;
        var pickups = context.Stops.Where(s => s.Kind == StopKind.Pickup).ToList();

        if (shipment.Execution == ExecutionStatus.Departed)
        {
            var origin = pickups.LastOrDefault(s => s.Status == StopStatus.Departed)?.Point;
            var moving = context.Fix.SpeedKph is { } speed && speed >= context.Settings.Health.MovingSpeedKph;
            if (moving || (origin is { } o && Geo.DistanceKm(o, context.Point) > 2))
            {
                shipment.SetExecution(ExecutionStatus.InTransit);
                Achieve(context, MilestoneType.InTransit, null, at, EventSource.Gps);
                timeline.Add(shipment, ShipmentEventTypes.InTransit, "In transit", at, EventSource.Gps, context.Fix.Latitude, context.Fix.Longitude);
            }
        }

        if (shipment.Execution is ExecutionStatus.Departed or ExecutionStatus.InTransit && pickups.All(s => s.Status is StopStatus.Departed or StopStatus.Skipped))
        {
            var next = context.Stops.Where(s => s.Kind == StopKind.Drop && s.Status is StopStatus.Pending or StopStatus.Approaching).OrderBy(s => s.Sequence).FirstOrDefault();
            if (next is { Status: StopStatus.Pending, Point: { } target } && Geo.DistanceKm(context.Point, target) <= context.Settings.Geofence.ApproachingKm)
            {
                next.Approach();
                var final = context.Stops.Where(s => s.Kind == StopKind.Drop).MaxBy(s => s.Sequence) == next;
                Achieve(context, final ? MilestoneType.ApproachingDestination : MilestoneType.ApproachingStop, next.Id, at, EventSource.Gps);
                if (final)
                {
                    shipment.SetExecution(ExecutionStatus.ApproachingDestination);
                }

                timeline.Add(shipment, ShipmentEventTypes.ApproachingStop, $"Approaching {next.Name}", at, EventSource.Gps, context.Fix.Latitude, context.Fix.Longitude, stopId: next.Id);
            }
        }

        return Task.CompletedTask;
    }
}

internal interface IGeofenceService
{
    Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken);

    /// <summary>The trip is ending: arrivals that were seen but not yet confirmed are taken as real.</summary>
    Task FlushAsync(TrackingContext context, DateTimeOffset at, CancellationToken cancellationToken);
}

internal sealed class GeofenceService(
    TrackingDbContext db, Timeline timeline, IMilestoneDetectionService milestones, ITrackingEventPublisher publisher, ITrackingDeliveryIntegration delivery, ITrackingTransporterIntegration transporter,
    ITrackingAlertService alerts) : IGeofenceService
{
    private static readonly HashSet<GeofenceType> BusinessPlaces = [GeofenceType.Warehouse, GeofenceType.Hub, GeofenceType.CrossDock, GeofenceType.Depot, GeofenceType.Customer, GeofenceType.Origin, GeofenceType.Destination];

    public async Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        var rules = context.Settings.Geofence;
        var fix = context.Fix;
        var stopFences = context.Stops.Where(s => s.GeofenceId is not null).Select(s => s.GeofenceId!.Value).ToHashSet();

        foreach (var stop in context.Stops.Where(s => s.HasLocation && s.Status != StopStatus.Departed && s.Status != StopStatus.Skipped))
        {
            var shape = stop.GeofenceId is { } id && context.SharedGeofences.FirstOrDefault(g => g.Id == id) is { } shared ? shared.Shape : new GeofenceShape(stop.Point!.Value, stop.RadiusM);
            var presence = PresenceFor(context, stop.Id, isStop: true);
            var state = presence.ToState();
            var transition = GeofenceEvaluator.Observe(state, shape, context.Point, fix.AccuracyMeters, fix.CapturedAt, rules);
            presence.Keep(state);
            if (transition is not null)
            {
                await OnStopAsync(context, stop, transition, cancellationToken);
            }
        }

        foreach (var fence in context.SharedGeofences.Where(g => !stopFences.Contains(g.Id)))
        {
            context.Presences.TryGetValue(fence.Id, out var existing);
            if (existing is null && Geo.DistanceMetres(new GeoPoint(fence.CenterLatitude, fence.CenterLongitude), context.Point) > fence.RadiusMeters + 2000 && fence.Polygon is null)
            {
                continue; // far from it and never near: nothing to remember
            }

            var presence = existing ?? PresenceFor(context, fence.Id, isStop: false);
            var state = presence.ToState();
            var transition = GeofenceEvaluator.Observe(state, fence.Shape, context.Point, fix.AccuracyMeters, fix.CapturedAt, rules);
            presence.Keep(state);
            if (transition is not null)
            {
                await OnSharedAsync(context, fence, transition, cancellationToken);
            }
        }
    }

    public async Task FlushAsync(TrackingContext context, DateTimeOffset at, CancellationToken cancellationToken)
    {
        foreach (var stop in context.Stops.Where(s => s.HasLocation && s.Status is StopStatus.Pending or StopStatus.Approaching))
        {
            if (context.Presences.TryGetValue(stop.Id, out var presence))
            {
                var state = presence.ToState();
                var transition = GeofenceEvaluator.Flush(state, at);
                presence.Keep(state);
                if (transition is not null)
                {
                    await OnStopAsync(context, stop, transition, cancellationToken);
                }
            }
        }
    }

    /// <summary>The place the vehicle is standing in, if it is standing in one the business knows: its kind and name.</summary>
    public static (string Kind, string Name)? KnownPlace(TrackingContext context)
    {
        foreach (var stop in context.Stops.Where(s => s.Status == StopStatus.Arrived))
        {
            if (context.Presences.TryGetValue(stop.Id, out var p) && p.State is Presence.Inside or Presence.ExitCandidate)
            {
                return (stop.PlaceType.ToString(), stop.Name);
            }
        }

        foreach (var fence in context.SharedGeofences.Where(g => BusinessPlaces.Contains(g.Type)))
        {
            if (context.Presences.TryGetValue(fence.Id, out var p) && p.State is Presence.Inside or Presence.ExitCandidate)
            {
                return (fence.Type.ToString(), fence.Name);
            }
        }

        return null;
    }

    private GeofencePresence PresenceFor(TrackingContext context, Guid subject, bool isStop)
    {
        if (!context.Presences.TryGetValue(subject, out var presence))
        {
            presence = GeofencePresence.Create(context.TenantId, context.Shipment.Id, subject, isStop);
            db.Presences.Add(presence);
            context.Presences[subject] = presence;
        }

        return presence;
    }

    private async Task OnStopAsync(TrackingContext context, ShipmentStop stop, GeofenceTransition transition, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        var at = transition.At;
        var fix = context.Fix;
        var code = stop.Reference ?? stop.Name;
        var first = context.Stops.Where(s => s.Kind == StopKind.Pickup).MinBy(s => s.Sequence) == stop;
        var finalDrop = stop.Kind == StopKind.Drop && context.Stops.Where(s => s.Kind == StopKind.Drop).MaxBy(s => s.Sequence) == stop;
        db.GeofenceEvents.Add(GeofenceEvent.Create(shipment, stop.GeofenceId, stop.Id, code, stop.Name, stop.PlaceType, transition.Type, at, fix.Latitude, fix.Longitude, fix.AccuracyMeters, transition.Confidence));

        switch (transition.Type)
        {
            case GeofenceEventType.Entered:
                stop.Arrive(at);
                milestones.Achieve(context, stop.Kind == StopKind.Pickup ? (first ? MilestoneType.ArrivedOrigin : MilestoneType.ArrivedStop) : (finalDrop ? MilestoneType.ArrivedDestination : MilestoneType.ArrivedStop), stop.Id, at, EventSource.Gps);
                shipment.SetExecution(stop.Kind == StopKind.Pickup ? (first ? ExecutionStatus.ArrivedOrigin : shipment.Execution) : (finalDrop ? ExecutionStatus.ArrivedDestination : ExecutionStatus.InTransit));
                timeline.Add(shipment, ShipmentEventTypes.ArrivedStop, $"Arrived at {stop.Name}", at, EventSource.Gps, fix.Latitude, fix.Longitude, code, stop.Id, confidence: transition.Confidence);
                await publisher.PublishAsync(new TrackingVehicleArrived(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, at, stop.Name, stop.Kind.ToString()), cancellationToken);
                if (stop.Kind == StopKind.Drop)
                {
                    await delivery.PublishDeliveryMilestoneAsync(new DeliveryTrackingEvent(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, stop.OrderId, stop.Reference, "EnteredSite", at, fix.Latitude, fix.Longitude), cancellationToken);
                    await delivery.PublishDeliveryMilestoneAsync(new DeliveryTrackingEvent(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, stop.OrderId, stop.Reference, "ArrivedAtSite", at, fix.Latitude, fix.Longitude), cancellationToken);
                }

                if ((stop.WindowEnd ?? stop.PlannedArrival) is { } planned)
                {
                    var late = (decimal)Math.Round((at - planned).TotalMinutes);
                    await transporter.PublishTrackingPerformanceEventAsync(new TrackingPerformanceEvent(context.TenantId, shipment.TransporterId, shipment.ShipmentReference, shipment.TripReference,
                        stop.Kind == StopKind.Pickup ? "PickupDelay" : "DeliveryDelay", late, $"{stop.Name}: arrived {Timeline.Time(at)}, planned {Timeline.Time(planned)}", at), cancellationToken);
                }

                break;

            case GeofenceEventType.Exited:
                stop.Depart(at);
                milestones.Achieve(context, stop.Kind == StopKind.Pickup ? (first ? MilestoneType.DepartedOrigin : MilestoneType.DepartedStop) : MilestoneType.DepartedStop, stop.Id, at, EventSource.Gps);
                if (stop.Kind == StopKind.Pickup && context.Stops.Where(s => s.Kind == StopKind.Pickup).All(s => s.Status is StopStatus.Departed or StopStatus.Skipped))
                {
                    shipment.SetExecution(ExecutionStatus.Departed);
                }
                else if (stop.Kind == StopKind.Drop && !finalDrop)
                {
                    shipment.SetExecution(ExecutionStatus.InTransit);
                }

                timeline.Add(shipment, ShipmentEventTypes.DepartedStop, $"Left {stop.Name}", at, EventSource.Gps, fix.Latitude, fix.Longitude, code, stop.Id, confidence: transition.Confidence);
                await publisher.PublishAsync(new TrackingVehicleDeparted(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, at, stop.Name, stop.Kind.ToString()), cancellationToken);
                if (stop.Kind == StopKind.Drop)
                {
                    await delivery.PublishDeliveryMilestoneAsync(new DeliveryTrackingEvent(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, stop.OrderId, stop.Reference, "DepartedSite", at, fix.Latitude, fix.Longitude), cancellationToken);
                }

                break;

            default:
                timeline.Add(shipment, ShipmentEventTypes.StayedInGeofence, $"Still at {stop.Name}", at, EventSource.Gps, fix.Latitude, fix.Longitude, code, stop.Id, confidence: transition.Confidence);
                break;
        }
    }

    private async Task OnSharedAsync(TrackingContext context, Geofence fence, GeofenceTransition transition, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        var fix = context.Fix;
        db.GeofenceEvents.Add(GeofenceEvent.Create(shipment, fence.Id, null, fence.Code, fence.Name, fence.Type, transition.Type, transition.At, fix.Latitude, fix.Longitude, fix.AccuracyMeters, transition.Confidence));
        var verb = transition.Type switch { GeofenceEventType.Entered => "Entered", GeofenceEventType.Exited => "Left", _ => "Still in" };
        timeline.Add(shipment, transition.Type switch { GeofenceEventType.Entered => ShipmentEventTypes.EnteredGeofence, GeofenceEventType.Exited => ShipmentEventTypes.ExitedGeofence, _ => ShipmentEventTypes.StayedInGeofence },
            $"{verb} {fence.Name}", transition.At, EventSource.Gps, fix.Latitude, fix.Longitude, fence.Code, confidence: transition.Confidence);

        if (transition.Type == GeofenceEventType.Entered)
        {
            await publisher.PublishAsync(new EnteredGeofence(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, transition.At, fence.Code, fence.Type.ToString()), cancellationToken);
            if (fence.Type is GeofenceType.RestrictedArea or GeofenceType.HighRiskZone)
            {
                await alerts.RaiseAsync(context, AlertType.GeofenceException, $"geofence:{shipment.Id:N}:{fence.Id:N}:{transition.At.UtcTicks}", $"{shipment.VehicleReference} entered {fence.Name} ({fence.Type}).",
                    fence.Type == GeofenceType.HighRiskZone ? Severity.High : Severity.Warning, cancellationToken);
            }
        }
        else if (transition.Type == GeofenceEventType.Exited)
        {
            await publisher.PublishAsync(new ExitedGeofence(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, transition.At, fence.Code, fence.Type.ToString()), cancellationToken);
        }
    }
}

internal interface IRouteDeviationService
{
    Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken);
}

internal sealed class RouteDeviationService(TrackingDbContext db, Timeline timeline, ITrackingEventPublisher publisher, ITrackingTransporterIntegration transporter, ITrackingAlertService alerts) : IRouteDeviationService
{
    public async Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        if (!context.HasUsableRoute || context.OffRouteKm is not { } off)
        {
            return;
        }

        var shipment = context.Shipment;
        var fix = context.Fix;
        // At a stop the vehicle is where it should be, even if the building is a little off the drawn route.
        var atStop = context.Stops.Any(s => context.Presences.TryGetValue(s.Id, out var p) && p.State is Presence.Inside or Presence.EntryCandidate);
        var state = shipment.DeviationMemory;
        var result = DeviationTracker.Observe(state, atStop ? 0 : off, fix.AccuracyMeters, fix.CapturedAt, context.Settings.Route);
        shipment.KeepDeviation(state);

        switch (result.Change)
        {
            case DeviationChange.Opened:
            {
                var deviation = RouteDeviation.Open(shipment, result.StartedAt ?? fix.CapturedAt, fix.Latitude, fix.Longitude, result.MaxOffKm, result.DurationMinutes, result.Severity);
                db.Deviations.Add(deviation);
                context.OpenDeviation = deviation;
                await alerts.RaiseAsync(context, AlertType.RouteDeviation, $"deviation:{deviation.Id:N}",
                    $"{shipment.VehicleReference} is {result.MaxOffKm:0.#} km from the planned route and has been for {result.DurationMinutes} min.", result.Severity, cancellationToken);
                timeline.Add(shipment, ShipmentEventTypes.RouteDeviation, $"Left the planned route ({result.MaxOffKm:0.#} km off)", result.StartedAt ?? fix.CapturedAt, EventSource.Gps, fix.Latitude, fix.Longitude);
                await publisher.PublishAsync(new RouteDeviationDetected(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, fix.CapturedAt, (decimal)Math.Round(result.MaxOffKm, 2)), cancellationToken);
                await transporter.PublishTrackingPerformanceEventAsync(new TrackingPerformanceEvent(context.TenantId, shipment.TransporterId, shipment.ShipmentReference, shipment.TripReference, "RouteDeviation", (decimal)Math.Round(result.MaxOffKm, 2), $"{result.DurationMinutes} min", fix.CapturedAt), cancellationToken);
                break;
            }

            case DeviationChange.Updated when context.OpenDeviation is { } open:
                open.Update(result.MaxOffKm, result.DurationMinutes, result.Severity);
                await alerts.RaiseAsync(context, AlertType.RouteDeviation, $"deviation:{open.Id:N}",
                    $"{shipment.VehicleReference} is {result.MaxOffKm:0.#} km from the planned route and has been for {result.DurationMinutes} min.", result.Severity, cancellationToken);
                break;

            case DeviationChange.Resolved:
            {
                context.OpenDeviation?.Resolve(fix.CapturedAt, result.DurationMinutes);
                if (context.OpenDeviation is { } closed)
                {
                    await alerts.ResolveAsync(context, $"deviation:{closed.Id:N}", "The vehicle is back on the planned route.", cancellationToken);
                }

                context.OpenDeviation = null;
                timeline.Add(shipment, ShipmentEventTypes.RouteResumed, $"Back on the planned route after {result.DurationMinutes} min", fix.CapturedAt, EventSource.Gps, fix.Latitude, fix.Longitude);
                await publisher.PublishAsync(new RouteDeviationResolved(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, fix.CapturedAt, result.DurationMinutes), cancellationToken);
                break;
            }
        }
    }
}

internal interface IDwellDetectionService
{
    Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken);

    Task CloseAsync(TrackingContext context, DateTimeOffset at, CancellationToken cancellationToken);
}

internal sealed class DwellDetectionService(
    TrackingDbContext db, Timeline timeline, IMilestoneDetectionService milestones, ITrackingEventPublisher publisher, ITrackingTransporterIntegration transporter, ITrackingAlertService alerts)
    : IDwellDetectionService
{
    public async Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        var fix = context.Fix;
        var rules = context.Settings.Dwell;
        var place = GeofenceService.KnownPlace(context);
        var state = shipment.DwellMemory;
        var results = DwellTracker.Observe(state, context.Point, fix.SpeedKph, fix.CapturedAt, place?.Kind, place?.Name, rules);
        shipment.KeepDwell(state);

        foreach (var result in results)
        {
            switch (result.Change)
            {
                case DwellChange.Started:
                {
                    var stop = place is null ? null : context.Stops.FirstOrDefault(s => s.Name == place.Value.Name && s.Status == StopStatus.Arrived);
                    var dwell = DwellEvent.Open(shipment, stop?.Id, result.Where, fix.Latitude, fix.Longitude, result.StartedAt ?? fix.CapturedAt, result.Kind, result.Kind == DwellKind.PlannedStop ? rules.Expected(place!.Value.Kind) : 0);
                    db.Dwells.Add(dwell);
                    context.OpenDwell = dwell;
                    if (stop is { Kind: StopKind.Pickup } && shipment.Execution == ExecutionStatus.ArrivedOrigin)
                    {
                        shipment.SetExecution(ExecutionStatus.Loading);
                        milestones.Achieve(context, MilestoneType.LoadingStarted, stop.Id, dwell.StartAt, EventSource.Gps);
                    }

                    break;
                }

                case DwellChange.Excess when context.OpenDwell is { } open:
                    open.Progress(result.DurationMinutes, result.ExcessMinutes);
                    await alerts.RaiseAsync(context, AlertType.ExcessiveDwell, $"dwell:{open.Id:N}",
                        $"{shipment.VehicleReference} has been at {result.Where} for {result.DurationMinutes} min, {result.ExcessMinutes} min over the {result.ExpectedMinutes} min expected.", null, cancellationToken);
                    timeline.Add(shipment, ShipmentEventTypes.ExcessiveDwell, $"Excess time at {result.Where}: {result.ExcessMinutes} min over", fix.CapturedAt, EventSource.Gps, fix.Latitude, fix.Longitude);
                    await publisher.PublishAsync(new ExcessiveDwellDetected(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, fix.CapturedAt, result.Where ?? "stop", result.ExcessMinutes), cancellationToken);
                    await transporter.PublishTrackingPerformanceEventAsync(new TrackingPerformanceEvent(context.TenantId, shipment.TransporterId, shipment.ShipmentReference, shipment.TripReference, "ExcessDwell", result.ExcessMinutes, result.Where, fix.CapturedAt), cancellationToken);
                    break;

                case DwellChange.UnplannedStop when context.OpenDwell is { } open:
                    open.Progress(result.DurationMinutes, result.DurationMinutes);
                    await alerts.RaiseAsync(context, AlertType.UnplannedStop, $"unplanned:{open.Id:N}", $"{shipment.VehicleReference} has been stopped for {result.DurationMinutes} min away from any planned stop.", null, cancellationToken);
                    timeline.Add(shipment, ShipmentEventTypes.UnplannedStop, $"Stopped for {result.DurationMinutes} min away from any planned stop", fix.CapturedAt, EventSource.Gps, fix.Latitude, fix.Longitude);
                    await publisher.PublishAsync(new UnplannedStopDetected(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, fix.CapturedAt, result.DurationMinutes), cancellationToken);
                    await transporter.PublishTrackingPerformanceEventAsync(new TrackingPerformanceEvent(context.TenantId, shipment.TransporterId, shipment.ShipmentReference, shipment.TripReference, "UnplannedStop", result.DurationMinutes, null, fix.CapturedAt), cancellationToken);
                    break;

                case DwellChange.Ended:
                    await EndAsync(context, result, cancellationToken);
                    break;
            }
        }

        if (state.Open && context.OpenDwell is { } ongoing && state.Since is { } since)
        {
            var minutes = (int)(fix.CapturedAt - since).TotalMinutes;
            ongoing.Progress(minutes, state.PlannedStop ? Math.Max(0, minutes - rules.Expected(state.Where ?? "Default")) : minutes);
        }
    }

    public async Task CloseAsync(TrackingContext context, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var state = context.Shipment.DwellMemory;
        if (DwellTracker.Close(state, context.Settings.Dwell) is { } result)
        {
            context.Shipment.KeepDwell(state);
            await EndAsync(context, result, cancellationToken);
        }
    }

    private static Task EndAsync(TrackingContext context, DwellResult result, CancellationToken cancellationToken)
    {
        if (context.OpenDwell is { } open)
        {
            open.Close(result.EndedAt ?? context.Fix?.CapturedAt ?? DateTimeOffset.UtcNow, result.DurationMinutes);
            foreach (var alert in context.Alerts.Where(a => a.Status != AlertStatus.Resolved && (a.DedupeKey == $"dwell:{open.Id:N}" || a.DedupeKey == $"unplanned:{open.Id:N}")).ToList())
            {
                alert.Resolve("The vehicle has moved on.", result.EndedAt ?? DateTimeOffset.UtcNow);
                if (alert.ExceptionId is { } id && context.Exceptions.FirstOrDefault(e => e.Id == id) is { } exception)
                {
                    exception.ConditionCleared(result.EndedAt ?? DateTimeOffset.UtcNow);
                }
            }

            context.OpenDwell = null;
        }

        return Task.CompletedTask;
    }
}
