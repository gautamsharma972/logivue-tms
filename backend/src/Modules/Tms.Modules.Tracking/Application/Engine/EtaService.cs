using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Tracking.Application.Engine;

internal interface IShipmentEtaService
{
    /// <summary>Recalculates arrival at every stop still ahead and the trip's risk. Does nothing when nothing has changed enough to be worth keeping, unless forced.</summary>
    Task UpdateAsync(TrackingContext context, bool force, CancellationToken cancellationToken, DateTimeOffset? asOf = null);
}

internal sealed class ShipmentEtaService(TrackingDbContext db, Timeline timeline, ITrackingEventPublisher publisher, ITrackingAlertService alerts) : IShipmentEtaService
{
    public async Task UpdateAsync(TrackingContext context, bool force, CancellationToken cancellationToken, DateTimeOffset? asOf = null)
    {
        var shipment = context.Shipment;
        if (!shipment.IsActive || shipment.LastLatitude is not { } lat || shipment.LastLongitude is not { } lon)
        {
            return;
        }

        var rules = context.Settings.Eta;
        var now = context.Now;
        var position = new GeoPoint(lat, lon);
        var stops = context.Stops.Where(s => s.Status != StopStatus.Skipped).Select(s => new EtaStop(
            s.Id, s.Sequence, s.Kind, s.Name, s.Point, s.AlongKm, s.PlannedArrival, s.WindowEnd, s.ExpectedDwellMinutes ?? context.Settings.Dwell.Expected(s.PlaceType.ToString()), s.Status, s.ArrivedAt)).ToList();

        var input = new EtaInput(
            now, position, shipment.AlongKm, context.HasUsableRoute, string.Equals(shipment.RouteSource, "Estimate", StringComparison.OrdinalIgnoreCase), shipment.RecentMovingSpeedKph, shipment.RecentSpeedCount, stops, shipment.Tracking,
            shipment.DeviationOpen);
        var etas = EtaCalculator.Calculate(input, rules);
        if (etas.Count == 0)
        {
            return;
        }

        foreach (var eta in etas)
        {
            context.Stops.First(s => s.Id == eta.StopId).Estimate(eta);
            if (context.Milestones.FirstOrDefault(m => m.StopId == eta.StopId && m.Status is MilestoneStatus.Pending or MilestoneStatus.Estimated && m.Type is MilestoneType.ArrivedStop or MilestoneType.ArrivedDestination or MilestoneType.ArrivedOrigin) is { } milestone)
            {
                milestone.Estimate(eta.Eta);
            }
        }

        // The trip's own arrival is that of its last drop; a trip with no drop is judged by its last stop.
        var finalStop = context.Stops.Where(s => s.Kind == StopKind.Drop && s.Status != StopStatus.Skipped).MaxBy(s => s.Sequence) ?? context.Stops.MaxBy(s => s.Sequence)!;
        var final = etas.FirstOrDefault(e => e.StopId == finalStop.Id) ?? etas[^1];
        var previousRisk = shipment.Risk;
        var previousEta = shipment.SystemEtaAt;
        var changed = previousEta is null || Math.Abs((final.Eta - previousEta.Value).TotalMinutes) >= rules.MinChangeMinutes;
        var due = shipment.LastEtaRecordedAt is not { } last || (now - last).TotalMinutes >= rules.RecordEveryMinutes;

        // An operator's correction changes what people are shown and how late it is judged to be, but never the calculation underneath it.
        if (shipment.EtaOverrideAt is { } overridden)
        {
            var shifted = final.DelayMinutes + (int)Math.Round((overridden - final.Eta).TotalMinutes);
            final = final with { DelayMinutes = shifted, Risk = final.Risk == RiskStatus.Unknown ? RiskStatus.Unknown : EtaCalculator.Classify(shifted, rules), Level = EtaCalculator.LevelOf(EtaCalculator.Classify(shifted, rules)) };
        }

        shipment.SetEstimate(shipment.EtaOverrideAt is null ? final.Eta : etas.First(e => e.StopId == final.StopId).Eta, final.Confidence, final.DelayMinutes, final.Risk, shipment.LastEtaRecordedAt ?? now);
        if (context.HasUsableRoute && shipment.AlongKm is { } along && context.Route is { } route)
        {
            var finalAlong = finalStop.AlongKm ?? route.LengthKm;
            shipment.SetProgress(Math.Max(0, finalAlong - along), finalAlong > 0 ? Math.Clamp(along / finalAlong * 100, 0, 100) : null);
        }
        else
        {
            shipment.SetProgress(final.RemainingKm, shipment.TravelledKm + final.RemainingKm > 0 ? shipment.TravelledKm / (shipment.TravelledKm + final.RemainingKm) * 100 : null);
        }

        if (changed || due || force)
        {
            shipment.MarkEtaSeen(now);
            foreach (var eta in etas)
            {
                db.EtaPredictions.Add(EtaPrediction.Create(shipment, eta.StopId, eta.StopId == finalStop.Id, asOf ?? now, eta));
            }
        }

        if (previousEta is { } before && Math.Abs((final.Eta - before).TotalMinutes) >= rules.PublishChangeMinutes)
        {
            await publisher.PublishAsync(new EtaUpdated(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, now, final.Eta, final.DelayMinutes), cancellationToken);
            timeline.Add(shipment, ShipmentEventTypes.EtaUpdated, $"Expected arrival now {Timeline.Time(final.Eta)}", now, EventSource.System, reason: null, confidence: final.Confidence);
        }

        if (previousRisk != final.Risk)
        {
            timeline.Add(shipment, ShipmentEventTypes.RiskChanged, $"Delivery risk is now {final.Risk}", now, EventSource.System, confidence: final.Confidence);
            if (final.Risk > previousRisk && final.Risk == RiskStatus.AtRisk)
            {
                await publisher.PublishAsync(new ShipmentAtRisk(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, now, final.Eta, final.DelayMinutes), cancellationToken);
            }
            else if (final.Risk >= RiskStatus.Delayed && final.Risk > previousRisk)
            {
                await publisher.PublishAsync(new ShipmentDelayed(context.TenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, now, final.Eta, final.DelayMinutes), cancellationToken);
            }
        }

        await alerts.EvaluateAsync(context, cancellationToken);
    }
}
