using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Notifications;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Compliance;

public interface IComplianceService
{
    Task<ComplianceReportDto> GetReportAsync(long transporterId, CancellationToken cancellationToken = default);

    /// <summary>Re-evaluates compliance, raises alerts for new problems, and resolves alerts whose problem has cleared.</summary>
    Task<ComplianceEvaluationResult> EvaluateAsync(long? transporterId = null, CancellationToken cancellationToken = default);
}

public sealed class ComplianceService(
    IRepository<Transporter> transporters,
    IRepository<DocumentType> documentTypes,
    IRepository<TransporterDocument> documents,
    IRepository<TransporterVehicle> vehicles,
    IRepository<TransporterDriver> drivers,
    IRepository<TransporterAlert> alerts,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<ComplianceService> logger) : IComplianceService
{
    private const string MissingAlertType = "COMPLIANCE_MISSING";
    private const string ExpiredAlertType = "COMPLIANCE_EXPIRED";
    private const string ExpiringAlertType = "COMPLIANCE_EXPIRING";

    /// <summary>Transporters evaluated per batch. Documents, vehicles, drivers and alerts load only for the batch.</summary>
    public const int EvaluationBatchSize = 200;

    public async Task<ComplianceReportDto> GetReportAsync(long transporterId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);

        var asOf = Today();
        var reminderDays = await settings.GetAsync<int>(SettingKeys.ComplianceRenewalReminderDays, cancellationToken);
        var types = await documentTypes.ListAsync(t => t.IsActive, cancellationToken);
        var docs = await documents.ListAsync(d => d.TransporterId == transporterId, cancellationToken);
        var fleet = await vehicles.ListAsync(v => v.TransporterId == transporterId, cancellationToken);
        var people = await drivers.ListAsync(d => d.TransporterId == transporterId, cancellationToken);

        return ComplianceEvaluator.Evaluate(transporterId, asOf, reminderDays, types, docs, fleet, people);
    }

    public async Task<ComplianceEvaluationResult> EvaluateAsync(long? transporterId = null, CancellationToken cancellationToken = default)
    {
        if (!await settings.GetAsync<bool>(SettingKeys.AlertsEnabled, cancellationToken))
        {
            logger.LogInformation("Compliance alerts are disabled by configuration; evaluation skipped");
            return new ComplianceEvaluationResult(0, 0, 0);
        }

        var asOf = Today();
        var reminderDays = await settings.GetAsync<int>(SettingKeys.ComplianceRenewalReminderDays, cancellationToken);
        var types = await documentTypes.ListAsync(t => t.IsActive, cancellationToken);
        var targets = await transporters.ListAsync(t =>
            t.Status != TransporterStatus.Deactivated
            && t.Status != TransporterStatus.Blacklisted
            && (transporterId == null || t.Id == transporterId), cancellationToken);

        var raised = 0;
        var resolved = 0;
        var pending = new List<TransporterAlert>();

        foreach (var batch in targets.Chunk(EvaluationBatchSize))
        {
            var ids = batch.Select(t => t.Id).ToList();
            var batchDocs = await documents.ListAsync(d => ids.Contains(d.TransporterId), cancellationToken);
            var batchVehicles = await vehicles.ListAsync(v => ids.Contains(v.TransporterId), cancellationToken);
            var batchDrivers = await drivers.ListAsync(d => ids.Contains(d.TransporterId), cancellationToken);
            var openAlerts = await alerts.ListAsync(a => ids.Contains(a.TransporterId) && a.Status != AlertStatus.Resolved
                && a.AlertType.StartsWith("COMPLIANCE_"), cancellationToken);

            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

            foreach (var transporter in batch)
            {
                var report = ComplianceEvaluator.Evaluate(
                    transporter.Id, asOf, reminderDays, types,
                    batchDocs.Where(d => d.TransporterId == transporter.Id).ToList(),
                    batchVehicles.Where(v => v.TransporterId == transporter.Id).ToList(),
                    batchDrivers.Where(d => d.TransporterId == transporter.Id).ToList());

                var desired = report.Items.Where(i => i.State is ComplianceItemState.Missing or ComplianceItemState.Expired or ComplianceItemState.ExpiringSoon)
                    .Select(i => DesiredAlert(transporter, i))
                    .ToList();

                var currentOpen = openAlerts.Where(a => a.TransporterId == transporter.Id).ToList();

                foreach (var wanted in desired)
                {
                    if (currentOpen.Any(a => a.AlertType == wanted.AlertType && a.EntityType == wanted.EntityType && a.EntityId == wanted.EntityId))
                    {
                        continue;
                    }

                    alerts.Add(wanted);
                    pending.Add(wanted);
                    raised++;
                }

                foreach (var stale in currentOpen.Where(a => !desired.Any(d => d.AlertType == a.AlertType && d.EntityType == a.EntityType && d.EntityId == a.EntityId)))
                {
                    stale.Status = AlertStatus.Resolved;
                    stale.ResolvedAt = clock.GetUtcNow().UtcDateTime;
                    resolved++;
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var alert in pending)
        {
            await notifications.SendAsync(new NotificationRequest(NotificationChannel.InApp, "transport-compliance",
                $"Compliance alert: {alert.AlertType}", alert.Message, alert.EntityType, alert.EntityId), cancellationToken);
        }

        logger.LogInformation("Compliance evaluated for {TransporterCount} transporters: {Raised} raised, {Resolved} resolved",
            targets.Count, raised, resolved);

        return new ComplianceEvaluationResult(raised, resolved, targets.Count);
    }

    private TransporterAlert DesiredAlert(Transporter transporter, ComplianceItemDto item)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var vehicleSuffix = item.VehicleId is { } v ? $" (vehicle {v})" : string.Empty;
        var driverSuffix = item.DriverId is { } d ? $" (driver {d})" : string.Empty;
        var scopeSuffix = vehicleSuffix + driverSuffix;

        return item.State switch
        {
            ComplianceItemState.Expired => new TransporterAlert
            {
                AlertType = ExpiredAlertType,
                Severity = item.BlocksAllocation ? Severity.Critical : Severity.High,
                TransporterId = transporter.Id,
                EntityType = "TransporterDocument",
                EntityId = item.DocumentId?.ToString() ?? string.Empty,
                Message = item.Message + scopeSuffix,
                CreatedAt = now,
                Status = AlertStatus.Open
            },
            ComplianceItemState.ExpiringSoon => new TransporterAlert
            {
                AlertType = ExpiringAlertType,
                Severity = item.DaysToExpiry is <= 7 ? Severity.High : Severity.Medium,
                TransporterId = transporter.Id,
                EntityType = "TransporterDocument",
                EntityId = item.DocumentId?.ToString() ?? string.Empty,
                Message = item.Message + scopeSuffix,
                CreatedAt = now,
                DueAt = item.DaysToExpiry is { } days ? DateTime.SpecifyKind(Today().AddDays(days).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) : null,
                Status = AlertStatus.Open
            },
            _ => new TransporterAlert
            {
                AlertType = MissingAlertType,
                Severity = Severity.High,
                TransporterId = transporter.Id,
                EntityType = "TransporterCompliance",
                EntityId = $"{transporter.Id}:{item.DocumentTypeCode}:{item.VehicleId ?? 0}:{item.DriverId ?? 0}",
                Message = item.Message + scopeSuffix,
                CreatedAt = now,
                Status = AlertStatus.Open
            }
        };
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
