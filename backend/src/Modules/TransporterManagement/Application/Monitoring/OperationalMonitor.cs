using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Pod;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Monitoring;

/// <summary>
/// Raises an alert when a placement, pickup, delivery or POD is overdue, and resolves it once the record catches up.
/// Overdue means the planned time has passed without the event, so the alert needs no operator action to start.
/// </summary>
public interface IOperationalMonitor
{
    /// <summary>Returns the number of alerts raised. Alerts that no longer apply are resolved.</summary>
    Task<int> RaiseOverdueAlertsAsync(CancellationToken cancellationToken = default);
}

public sealed class OperationalMonitor(
    IRepository<VehiclePlacementRequest> placements,
    IRepository<LoadExecution> executions,
    IRepository<PodRecord> pods,
    IRepository<TransporterAlert> alerts,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    TimeProvider clock,
    ILogger<OperationalMonitor> logger) : IOperationalMonitor
{
    private const string PlacementType = "PLACEMENT_OVERDUE";
    private const string PickupType = "PICKUP_OVERDUE";
    private const string DeliveryType = "DELIVERY_OVERDUE";
    private const string PodType = "POD_OVERDUE";

    public async Task<int> RaiseOverdueAlertsAsync(CancellationToken cancellationToken = default)
    {
        if (!await settings.GetAsync<bool>(SettingKeys.AlertsEnabled, cancellationToken))
        {
            return 0;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var grace = TimeSpan.FromMinutes(await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken));
        var policy = await settings.GetAsync<DelayPolicySetting>(SettingKeys.ExecutionDelayPolicy, cancellationToken);
        var tolerance = TimeSpan.FromMinutes(policy.ToleranceMinutes);

        var desired = new List<TransporterAlert>();

        var openPlacements = await placements.ListAsync(p => p.Status == PlacementStatus.Requested || p.Status == PlacementStatus.Confirmed
            || p.Status == PlacementStatus.VehicleAssigned || p.Status == PlacementStatus.Reported, cancellationToken);
        desired.AddRange(openPlacements.Where(p => now > p.RequiredPlacementAt + grace)
            .Select(p => Alert(PlacementType, Severity.High, p.TransporterId, p.LoadReference, "VehiclePlacement", p.Id,
                $"Vehicle placement for load {p.LoadReference} is overdue; it was due {p.RequiredPlacementAt:yyyy-MM-dd HH:mm} UTC.", now)));

        var loads = await executions.ListAsync(e => e.ActualPickupAt == null || e.ActualDeliveryAt == null, cancellationToken);
        desired.AddRange(loads.Where(e => e.PlannedPickupAt is { } p && e.ActualPickupAt == null && now > p + tolerance)
            .Select(e => Alert(PickupType, Severity.Medium, e.TransporterId, e.LoadReference, "LoadExecution", e.Id,
                $"Pickup for load {e.LoadReference} is overdue; it was planned for {e.PlannedPickupAt:yyyy-MM-dd HH:mm} UTC.", now)));
        desired.AddRange(loads.Where(e => e.PlannedDeliveryAt is { } d && e.ActualDeliveryAt == null && now > d + tolerance)
            .Select(e => Alert(DeliveryType, Severity.High, e.TransporterId, e.LoadReference, "LoadExecution", e.Id,
                $"Delivery for load {e.LoadReference} is overdue; it was planned for {e.PlannedDeliveryAt:yyyy-MM-dd HH:mm} UTC.", now)));

        var duePods = await pods.ListAsync(p => p.Status == PodStatus.Pending && p.DueAt < now, cancellationToken);
        desired.AddRange(duePods.Select(p => Alert(PodType, Severity.Medium, p.TransporterId, p.LoadReference, "PodRecord", p.Id,
            $"POD for load {p.LoadReference} is overdue; it was due {p.DueAt:yyyy-MM-dd HH:mm} UTC.", now)));

        var open = await alerts.ListAsync(a => a.Status != AlertStatus.Resolved && (a.AlertType == PlacementType || a.AlertType == PickupType
            || a.AlertType == DeliveryType || a.AlertType == PodType), cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var raised = 0;
        foreach (var wanted in desired)
        {
            if (open.Any(a => a.AlertType == wanted.AlertType && a.EntityType == wanted.EntityType && a.EntityId == wanted.EntityId))
            {
                continue;
            }

            alerts.Add(wanted);
            raised++;
        }

        var resolved = 0;
        foreach (var stale in open.Where(a => !desired.Any(d => d.AlertType == a.AlertType && d.EntityType == a.EntityType && d.EntityId == a.EntityId)))
        {
            stale.Status = AlertStatus.Resolved;
            stale.ResolvedAt = now;
            resolved++;
        }

        await audit.RecordAsync(new AuditEntry("OperationalMonitor", "run", "OverdueAlertsEvaluated",
            NewValueJson: AuditJson.Serialize(new { raised, resolved })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (raised > 0 || resolved > 0)
        {
            logger.LogInformation("Overdue monitor raised {Raised} and resolved {Resolved} alert(s)", raised, resolved);
        }

        return raised;
    }

    private static TransporterAlert Alert(string type, Severity severity, long transporterId, string loadReference, string entityType, long entityId, string message, DateTime now) => new()
    {
        AlertType = type,
        Severity = severity,
        TransporterId = transporterId,
        LoadReference = loadReference,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        Message = message,
        CreatedAt = now,
        Status = AlertStatus.Open
    };
}
