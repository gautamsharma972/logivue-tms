using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Operations;

/// <summary>
/// Raises alerts for things that need attention and closes them when the record catches up. A problem is raised once (same type and key); an alert
/// that no longer applies resolves itself. Overdue means the planned time has passed without the event, so nobody has to notice first.
/// </summary>
internal sealed class AlertService(
    TransportersDbContext db, IShipmentOperationsFeed feed, ITransporterSettings settings, ICurrentUser user, TimeProvider clock, ILogger<AlertService> logger)
{
    public const string PlacementOverdue = "PLACEMENT_OVERDUE";
    public const string PickupOverdue = "PICKUP_OVERDUE";
    public const string DeliveryOverdue = "DELIVERY_OVERDUE";
    public const string PodOverdue = "POD_OVERDUE";
    public const string PlacementNoShow = "PLACEMENT_NO_SHOW";
    public const string CarrierPickupDelay = "CARRIER_PICKUP_DELAY";
    public const string CarrierDeliveryDelay = "CARRIER_DELIVERY_DELAY";
    public const string DelayAttributionRequired = "DELAY_ATTRIBUTION_REQUIRED";

    private static readonly string[] OverdueTypes = [PlacementOverdue, PickupOverdue, DeliveryOverdue, PodOverdue];

    /// <summary>How far back the monitor looks, so a long-forgotten record does not raise alerts for ever.</summary>
    private const int LookbackDays = 60;

    /// <summary>Adds an alert unless the same problem is already open. Does not save.</summary>
    public async Task RaiseAsync(string type, AlertSeverity severity, Guid transporterId, Guid? shipmentId, string? number, string entityKey, string message, CancellationToken cancellationToken)
    {
        if (user.TenantId is not { } tenantId || !await settings.GetAsync<bool>(SettingKeys.AlertsEnabled, cancellationToken))
        {
            return;
        }

        var exists = db.Alerts.Local.Any(a => a.AlertType == type && a.EntityKey == entityKey && a.Status != AlertStatus.Resolved)
            || await db.Alerts.AnyAsync(a => a.AlertType == type && a.EntityKey == entityKey && a.Status != AlertStatus.Resolved, cancellationToken);
        if (!exists)
        {
            db.Alerts.Add(TransporterAlert.Raise(tenantId, type, severity, transporterId, shipmentId, number, entityKey, message));
        }
    }

    /// <summary>Closes the open alerts of the given types for one thing. Does not save.</summary>
    public async Task ResolveAsync(string entityKey, string[] types, CancellationToken cancellationToken)
    {
        var open = await db.Alerts.Where(a => a.EntityKey == entityKey && types.Contains(a.AlertType) && a.Status != AlertStatus.Resolved).ToListAsync(cancellationToken);
        foreach (var alert in open)
        {
            alert.Resolve(clock.GetUtcNow(), "No longer applies.");
        }
    }

    /// <summary>Keeps the delay alerts of a load in step with how its pickup and delivery are attributed. Does not save.</summary>
    public async Task SyncDelaysAsync(LoadExecution e, CancellationToken cancellationToken)
    {
        foreach (var (delivery, attribution, minutes) in new[] { (false, e.PickupAttribution, e.PickupDelayMinutes), (true, e.DeliveryAttribution, e.DeliveryDelayMinutes) })
        {
            var key = $"exec:{e.Id:N}:{(delivery ? "delivery" : "pickup")}";
            var what = delivery ? "delivery" : "pickup";
            await ResolveAsync(key, [CarrierPickupDelay, CarrierDeliveryDelay, DelayAttributionRequired], cancellationToken);
            switch (attribution)
            {
                case DelayAttribution.Carrier:
                    await RaiseAsync(delivery ? CarrierDeliveryDelay : CarrierPickupDelay, AlertSeverity.Medium, e.TransporterId, e.ShipmentId, e.ShipmentNumber, key,
                        $"{e.ShipmentNumber}: {what} was {minutes} minutes late, attributed to the carrier.", cancellationToken);
                    break;
                case DelayAttribution.Unattributed:
                    await RaiseAsync(DelayAttributionRequired, AlertSeverity.Low, e.TransporterId, e.ShipmentId, e.ShipmentNumber, key,
                        $"{e.ShipmentNumber}: {what} was {minutes} minutes late and needs a delay reason.", cancellationToken);
                    break;
            }
        }
    }

    /// <summary>Raises the overdue alerts that apply now and resolves those that no longer do. Returns how many were raised.</summary>
    public async Task<int> EvaluateOverdueAsync(CancellationToken cancellationToken)
    {
        if (user.TenantId is not { } tenantId || !await settings.GetAsync<bool>(SettingKeys.AlertsEnabled, cancellationToken))
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var since = now.AddDays(-LookbackDays);
        var grace = TimeSpan.FromMinutes(await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken));
        var policy = await settings.GetAsync<DelayPolicySetting>(SettingKeys.ExecutionDelayPolicy, cancellationToken);
        var tolerance = TimeSpan.FromMinutes(policy.ToleranceMinutes);
        var podSla = TimeSpan.FromHours(await settings.GetAsync<int>(SettingKeys.PodSubmissionSlaHours, cancellationToken));

        var desired = new List<TransporterAlert>();
        var placements = await db.Placements.AsNoTracking().Where(p => (p.Status == PlacementStatus.VehicleAssigned || p.Status == PlacementStatus.Reported) && p.RequiredAt >= since).ToListAsync(cancellationToken);
        desired.AddRange(placements.Where(p => now > p.RequiredAt + grace).Select(p => TransporterAlert.Raise(
            tenantId, PlacementOverdue, AlertSeverity.High, p.TransporterId, p.ShipmentId, p.ShipmentNumber, $"placement:{p.Id:N}",
            $"The vehicle for {p.ShipmentNumber} is overdue at the pickup; it was due {p.RequiredAt.ToOffset(TimeSpan.FromMinutes(330)):dd MMM HH:mm}.")));

        var executions = await db.Executions.AsNoTracking().Where(e => e.Status != ExecutionStatus.Cancelled && (e.PlannedPickupAt >= since || e.PlannedDeliveryAt >= since)).ToListAsync(cancellationToken);
        desired.AddRange(executions.Where(e => e.PlannedPickupAt is { } p && e.ActualPickupAt is null && now > p + tolerance).Select(e => TransporterAlert.Raise(
            tenantId, PickupOverdue, AlertSeverity.Medium, e.TransporterId, e.ShipmentId, e.ShipmentNumber, $"exec:{e.Id:N}:pickup-overdue",
            $"Pickup for {e.ShipmentNumber} is overdue; it was planned for {e.PlannedPickupAt:dd MMM HH:mm}.")));
        desired.AddRange(executions.Where(e => e.PlannedDeliveryAt is { } d && e.ActualDeliveryAt is null && now > d + tolerance).Select(e => TransporterAlert.Raise(
            tenantId, DeliveryOverdue, AlertSeverity.High, e.TransporterId, e.ShipmentId, e.ShipmentNumber, $"exec:{e.Id:N}:delivery-overdue",
            $"Delivery for {e.ShipmentNumber} is overdue; it was planned for {e.PlannedDeliveryAt:dd MMM HH:mm}.")));

        foreach (var fact in await feed.ListAllAsync(since, now.AddDays(1), cancellationToken))
        {
            desired.AddRange(fact.Deliveries.Where(d => d.FirstProofAt is null && d.DeliveredAt + podSla < now).Take(1).Select(d => TransporterAlert.Raise(
                tenantId, PodOverdue, AlertSeverity.Medium, fact.TransporterId, fact.ShipmentId, fact.Number, $"pod:{fact.ShipmentId:N}",
                $"Proof of delivery for {fact.Number} is overdue; the load was delivered {d.DeliveredAt.ToOffset(TimeSpan.FromMinutes(330)):dd MMM HH:mm}.")));
        }

        var open = await db.Alerts.Where(a => a.Status != AlertStatus.Resolved && OverdueTypes.Contains(a.AlertType)).ToListAsync(cancellationToken);
        var raised = 0;
        foreach (var wanted in desired)
        {
            if (!open.Any(a => a.AlertType == wanted.AlertType && a.EntityKey == wanted.EntityKey))
            {
                db.Alerts.Add(wanted);
                raised++;
            }
        }

        var resolved = 0;
        foreach (var stale in open.Where(a => !desired.Any(d => d.AlertType == a.AlertType && d.EntityKey == a.EntityKey)))
        {
            stale.Resolve(now, "The record caught up.");
            resolved++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (raised > 0 || resolved > 0)
        {
            logger.LogInformation("Overdue monitor raised {Raised} and resolved {Resolved} alert(s)", raised, resolved);
        }

        return raised;
    }
}
