using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Engine;

/// <summary>
/// Notices when tracking has gone quiet and when an exception has waited too long. There is no worker that walks every tenant; this runs, at most every few seconds per tenant,
/// whenever someone reads the control tower, so a tenant nobody is watching costs nothing and one being watched is always current.
/// </summary>
internal sealed class TrackingHealthMonitor(
    TrackingDbContext db, ITrackingContextLoader loader, ITrackingSettings settings, ITrackingAlertService alerts, IShipmentEtaService eta, Timeline timeline, ITrackingEventPublisher publisher,
    ITrackingTransporterIntegration transporter, ITrackingNotificationService notifications, PendingEvents pending, ICurrentUser user, TimeProvider clock, IConfiguration configuration)
{
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> LastRun = new();
    private readonly TimeSpan _minimumGap = TimeSpan.FromSeconds(configuration.GetValue("Tracking:HealthCheckMinimumSeconds", 15));

    public async Task EvaluateAsync(CancellationToken cancellationToken, bool force = false)
    {
        if (user.TenantId is not { } tenantId)
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (!force && LastRun.TryGetValue(tenantId, out var last) && now - last < _minimumGap)
        {
            return;
        }

        LastRun[tenantId] = now;
        var snapshot = await settings.SnapshotAsync(cancellationToken);
        var rules = snapshot.Health;
        var open = await db.Sessions.AsNoTracking()
            .Where(s => s.Status == TrackingSessionStatus.Active || s.Status == TrackingSessionStatus.Stale || s.Status == TrackingSessionStatus.Lost)
            .Select(s => new { s.Id, s.TrackedShipmentId, s.Status, s.StartedAt, s.LastLocationAt }).ToListAsync(cancellationToken);

        var notify = new List<TrackingNotification>();
        foreach (var row in open)
        {
            var (health, age) = HealthEvaluator.Evaluate(row.Status, row.LastLocationAt, row.StartedAt, now, rules);
            var wasHealth = row.Status == TrackingSessionStatus.Stale ? TrackingHealth.Stale : row.Status == TrackingSessionStatus.Lost ? TrackingHealth.Lost : TrackingHealth.Healthy;
            if (health == wasHealth && health == TrackingHealth.Healthy)
            {
                continue;
            }

            var shipment = await db.Shipments.Include(s => s.Stops).FirstAsync(s => s.Id == row.TrackedShipmentId, cancellationToken);
            var session = await db.Sessions.FirstAsync(s => s.Id == row.Id, cancellationToken);
            var context = await loader.LoadAsync(shipment, session, now, cancellationToken);
            var since = row.LastLocationAt ?? row.StartedAt;
            var where = since.ToOffset(Clock.India).ToString("hh:mm tt", CultureInfo.InvariantCulture);

            if (health != wasHealth)
            {
                if (context.OpenGap is null)
                {
                    var gap = TrackingGap.Open(shipment, since, shipment.LastLatitude ?? 0, shipment.LastLongitude ?? 0, LocationPipeline.GapSeverity(age, rules), age);
                    db.Gaps.Add(gap);
                    context.OpenGap = gap;
                }

                var gapId = context.OpenGap.Id;
                if (health == TrackingHealth.Stale)
                {
                    session.MarkStale();
                    shipment.SetHealth(TrackingHealth.Stale);
                    await alerts.RaiseAsync(context, AlertType.TrackingStale, $"tracking-stale:{gapId:N}", $"Vehicle location unavailable since {where} ({age} min).", null, cancellationToken);
                    timeline.Add(shipment, ShipmentEventTypes.TrackingStale, $"No location since {where}", now, EventSource.System);
                    await publisher.PublishAsync(new TrackingStale(tenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, now, age), cancellationToken);
                }
                else
                {
                    session.MarkLost();
                    shipment.SetHealth(TrackingHealth.Lost);
                    await alerts.ResolveAsync(context, $"tracking-stale:{gapId:N}", "Tracking is now lost.", cancellationToken);
                    await alerts.RaiseAsync(context, AlertType.TrackingLost, $"tracking-lost:{gapId:N}", $"Vehicle location unavailable since {where} ({age} min). Last known position is on the map.", null, cancellationToken);
                    timeline.Add(shipment, ShipmentEventTypes.TrackingLost, $"Tracking lost: no location since {where}", now, EventSource.System);
                    await publisher.PublishAsync(new TrackingLost(tenantId, shipment.ShipmentId, shipment.ShipmentReference, shipment.TripReference, shipment.VehicleReference, shipment.TransporterId, now, age), cancellationToken);
                    await transporter.PublishTrackingPerformanceEventAsync(new TrackingPerformanceEvent(tenantId, shipment.TransporterId, shipment.ShipmentReference, shipment.TripReference, "TrackingCompliance", age, "Tracking lost", now), cancellationToken);
                }

                var key = string.IsNullOrWhiteSpace(shipment.VehicleReference) ? shipment.TripReference : shipment.VehicleReference!;
                if (await db.Positions.FirstOrDefaultAsync(p => p.VehicleReference == key, cancellationToken) is { } position)
                {
                    position.SetHealth(health, now);
                }

                await eta.UpdateAsync(context, force: true, cancellationToken);
            }

            context.OpenGap?.Grow(age, LocationPipeline.GapSeverity(age, rules));
            foreach (var e in pending.Drain())
            {
                shipment.Publish(e);
            }

            notify.AddRange(context.Notifications);
        }

        await EscalateAsync(snapshot, now, notify, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var notification in notify)
        {
            try
            {
                await notifications.NotifyAsync(notification, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // a push that fails is not a reason to lose what was saved
            }
        }
    }

    /// <summary>An exception nobody has resolved moves up the chain on its own once each step's time has passed.</summary>
    private async Task EscalateAsync(SettingsSnapshot snapshot, DateTimeOffset now, List<TrackingNotification> notify, CancellationToken cancellationToken)
    {
        var steps = snapshot.Alerts.Steps;
        if (steps.Count == 0)
        {
            return;
        }

        var waiting = await db.Exceptions.Where(e => e.Status == ExceptionStatus.Open || e.Status == ExceptionStatus.Acknowledged || e.Status == ExceptionStatus.InProgress || e.Status == ExceptionStatus.Escalated)
            .ToListAsync(cancellationToken);
        foreach (var exception in waiting)
        {
            if (exception.EscalationLevel >= steps.Count)
            {
                continue;
            }

            var step = steps[exception.EscalationLevel];
            if ((now - exception.RaisedAt).TotalMinutes >= step.AfterMinutes)
            {
                exception.EscalateAutomatically(step.Level, now);
                notify.Add(new TrackingNotification(exception.TenantId, "ExceptionEscalated", $"Escalated to {step.Level}: {exception.Number}", $"{exception.ShipmentReference}: {exception.Description}", Severity.High, null, exception.TripReference, exception.TransporterId));
            }
        }
    }
}
