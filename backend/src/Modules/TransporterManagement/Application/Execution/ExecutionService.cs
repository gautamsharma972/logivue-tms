using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Severity = LogiVue.Tms.TransporterManagement.Domain.Common.Severity;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Pod;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Execution;

/// <summary>
/// Operational execution of an accepted load. Events must follow the operational sequence and are recorded once each.
/// Each late pickup or delivery is attributed through the configured delay policy. Delivery opens a POD record when
/// the load requires one.
/// </summary>
public interface IExecutionService
{
    Task<LoadExecutionDto> CreateAsync(CreateExecutionRequest request, CancellationToken cancellationToken = default);

    Task<LoadExecutionDto> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<LoadExecutionDto> RecordEventAsync(long id, RecordExecutionEventRequest request, CancellationToken cancellationToken = default);
}

public sealed class ExecutionService(
    IRepository<LoadExecution> executions,
    IRepository<LoadExecutionEvent> events,
    IRepository<Tender> tenders,
    IRepository<VehiclePlacementRequest> placements,
    IRepository<PodRecord> pods,
    IRepository<TransporterAlert> alerts,
    IPerformanceService performance,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<CreateExecutionRequest> createValidator,
    IValidator<RecordExecutionEventRequest> eventValidator,
    ILogger<ExecutionService> logger) : IExecutionService
{
    public async Task<LoadExecutionDto> CreateAsync(CreateExecutionRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var load = request.LoadReference.Trim();

        if (await executions.AnyAsync(e => e.LoadReference == load && e.TransporterId == request.TransporterId, cancellationToken))
        {
            throw new ConflictException($"Load {load} already has an execution record for this transporter.", "EXECUTION_EXISTS");
        }

        var tender = (await tenders.ListAsync(t => t.LoadReference == load && t.TransporterId == request.TransporterId
            && (t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded), cancellationToken))
            .OrderByDescending(t => t.Id)
            .FirstOrDefault()
            ?? throw new BusinessRuleException("An execution record can be created only for a load the transporter has accepted or been awarded.", "EXECUTION_REQUIRES_ACCEPTED_LOAD");

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var execution = new LoadExecution
        {
            LoadReference = load,
            TransporterId = request.TransporterId,
            TenderId = tender.Id,
            PlannedPickupAt = tender.PickupDateTime,
            PlannedDeliveryAt = tender.DeliveryDateTime,
            PodRequired = request.PodRequired,
            Status = ExecutionStatus.NotStarted,
            CreatedAt = now,
            UpdatedAt = now
        };
        executions.Add(execution);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("LoadExecution", execution.Id.ToString(), "ExecutionCreated",
            NewValueJson: AuditJson.Serialize(new { load, request.TransporterId, execution.PodRequired })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Execution {ExecutionId} created for load {LoadReference}", execution.Id, load);
        return ToDto(execution, []);
    }

    public async Task<LoadExecutionDto> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var execution = await executions.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Execution {id} was not found.", "EXECUTION_NOT_FOUND");
        var history = await events.ListAsync(e => e.LoadExecutionId == id, cancellationToken);
        return ToDto(execution, history);
    }

    public async Task<LoadExecutionDto> RecordEventAsync(long id, RecordExecutionEventRequest request, CancellationToken cancellationToken = default)
    {
        await eventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var execution = await executions.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Execution {id} was not found.", "EXECUTION_NOT_FOUND");
        var history = await events.ListAsync(e => e.LoadExecutionId == id, cancellationToken);

        if (history.Count > 0)
        {
            var latest = history.OrderByDescending(e => e.EventAt).ThenByDescending(e => e.Id).First();
            if (history.Max(e => (int)e.EventType) >= (int)request.EventType)
            {
                throw new BusinessRuleException($"{request.EventType} cannot be recorded after {history.MaxBy(e => (int)e.EventType)!.EventType}.", "EXECUTION_SEQUENCE_INVALID");
            }

            if (request.EventAt < latest.EventAt)
            {
                throw new BusinessRuleException("An event cannot be earlier than the previous event on the same load.", "EXECUTION_TIME_ORDER");
            }
        }

        var policy = await settings.GetAsync<DelayPolicySetting>(SettingKeys.ExecutionDelayPolicy, cancellationToken);
        var reasonCode = request.DelayReasonCode?.Trim().ToUpperInvariant();
        var reason = reasonCode is null ? null
            : policy.Reasons.FirstOrDefault(r => r.Code == reasonCode)
              ?? throw new BusinessRuleException($"'{request.DelayReasonCode}' is not a valid delay reason.", "DELAY_REASON_INVALID");

        var now = clock.GetUtcNow().UtcDateTime;
        var sla = await settings.GetAsync<int>(SettingKeys.PodSubmissionSlaHours, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        events.Add(new LoadExecutionEvent
        {
            LoadExecutionId = execution.Id,
            EventType = request.EventType,
            EventAt = request.EventAt,
            DelayReasonCode = reason is null ? null : reasonCode,
            Remarks = Mapping.Clean(request.Remarks),
            RecordedBy = currentUser.UserId,
            RecordedAt = now
        });

        var alertsRaised = new List<(string Type, Severity Severity, string Message)>();
        switch (request.EventType)
        {
            case ExecutionEventType.VehicleDeparture:
                execution.ActualPickupAt = request.EventAt;
                (execution.PickupDelayMinutes, execution.PickupDelayReasonCode, execution.PickupAttribution) =
                    Evaluate(execution.PlannedPickupAt, request.EventAt, policy, reason, reasonCode);
                DelayAlert("PICKUP", execution, execution.PickupAttribution, execution.PickupDelayMinutes, alertsRaised);
                break;

            case ExecutionEventType.LoadingStart:
                var placed = (await placements.ListAsync(p => p.LoadReference == execution.LoadReference
                    && p.TransporterId == execution.TransporterId && p.Status == PlacementStatus.Placed, cancellationToken))
                    .OrderByDescending(p => p.Id)
                    .FirstOrDefault();
                if (placed is not null)
                {
                    placed.Status = PlacementStatus.LoadingStarted;
                    placed.LoadingStartedAt = request.EventAt;
                }

                break;

            case ExecutionEventType.DeliveryComplete:
                execution.ActualDeliveryAt = request.EventAt;
                (execution.DeliveryDelayMinutes, execution.DeliveryDelayReasonCode, execution.DeliveryAttribution) =
                    Evaluate(execution.PlannedDeliveryAt, request.EventAt, policy, reason, reasonCode);
                DelayAlert("DELIVERY", execution, execution.DeliveryAttribution, execution.DeliveryDelayMinutes, alertsRaised);

                if (execution.PodRequired)
                {
                    pods.Add(new PodRecord
                    {
                        LoadReference = execution.LoadReference,
                        TransporterId = execution.TransporterId,
                        LoadExecutionId = execution.Id,
                        DeliveredAt = request.EventAt,
                        DueAt = request.EventAt.AddHours(sla),
                        Status = PodStatus.Pending,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                break;
        }

        execution.Status = Max(execution.Status, StatusFor(request.EventType));
        execution.UpdatedAt = now;

        foreach (var alert in alertsRaised)
        {
            alerts.Add(new TransporterAlert
            {
                AlertType = alert.Type,
                Severity = alert.Severity,
                TransporterId = execution.TransporterId,
                LoadReference = execution.LoadReference,
                EntityType = "LoadExecution",
                EntityId = execution.Id.ToString(),
                Message = alert.Message,
                CreatedAt = now,
                Status = AlertStatus.Open
            });
        }

        await audit.RecordAsync(new AuditEntry("LoadExecution", execution.Id.ToString(), "ExecutionEventRecorded",
            NewValueJson: AuditJson.Serialize(new { request.EventType, request.EventAt, attribution = request.EventType is ExecutionEventType.VehicleDeparture ? execution.PickupAttribution : execution.DeliveryAttribution }),
            Reason: reasonCode), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(execution.TransporterId,
            new[] { execution.PlannedPickupAt, execution.PlannedDeliveryAt, request.EventAt }.Where(d => d is not null).Select(d => d!.Value),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var updated = await events.ListAsync(e => e.LoadExecutionId == id, cancellationToken);
        return ToDto(execution, updated);
    }

    /// <summary>
    /// Measures one event against its planned time. Early or on-time events carry no attribution. A late event carries
    /// the attribution of its reason, or <see cref="DelayAttribution.Unattributed"/> when none is given.
    /// </summary>
    public static (int? Minutes, string? ReasonCode, DelayAttribution Attribution) Evaluate(
        DateTime? planned, DateTime actual, DelayPolicySetting policy, DelayReasonSetting? reason, string? reasonCode)
    {
        if (planned is null)
        {
            return (null, null, DelayAttribution.None);
        }

        var minutes = (int)Math.Round((actual - planned.Value).TotalMinutes);
        if (minutes <= policy.ToleranceMinutes)
        {
            return (minutes, null, DelayAttribution.None);
        }

        return (minutes, reason is null ? null : reasonCode, reason is null ? DelayAttribution.Unattributed : ParseAttribution(reason.Attribution));
    }

    private static void DelayAlert(string kind, LoadExecution execution, DelayAttribution attribution, int? minutes, List<(string, Severity, string)> raised)
    {
        switch (attribution)
        {
            case DelayAttribution.Carrier:
                raised.Add(($"CARRIER_{kind}_DELAY", Severity.Medium,
                    $"Load {execution.LoadReference} {kind.ToLowerInvariant()} was {minutes} minutes late, attributed to the carrier."));
                break;
            case DelayAttribution.Unattributed:
                raised.Add(("DELAY_ATTRIBUTION_REQUIRED", Severity.Low,
                    $"Load {execution.LoadReference} {kind.ToLowerInvariant()} was {minutes} minutes late and needs a delay reason."));
                break;
        }
    }

    private static DelayAttribution ParseAttribution(string value) => value switch
    {
        "Carrier" => DelayAttribution.Carrier,
        "NonCarrier" => DelayAttribution.NonCarrier,
        _ => DelayAttribution.Unattributed
    };

    private static ExecutionStatus StatusFor(ExecutionEventType type) => type switch
    {
        ExecutionEventType.PickupAppointment or ExecutionEventType.VehicleArrival
            or ExecutionEventType.LoadingStart or ExecutionEventType.LoadingComplete => ExecutionStatus.AtPickup,
        ExecutionEventType.DeliveryComplete => ExecutionStatus.Delivered,
        _ => ExecutionStatus.PickedUp
    };

    private static ExecutionStatus Max(ExecutionStatus current, ExecutionStatus next) => next > current ? next : current;

    private static LoadExecutionDto ToDto(LoadExecution e, IReadOnlyCollection<LoadExecutionEvent> history) => new(
        e.Id, e.LoadReference, e.TransporterId, e.TenderId, e.PlannedPickupAt, e.ActualPickupAt, e.PickupDelayMinutes,
        e.PickupDelayReasonCode, e.PickupAttribution, e.PlannedDeliveryAt, e.ActualDeliveryAt, e.DeliveryDelayMinutes,
        e.DeliveryDelayReasonCode, e.DeliveryAttribution, e.PodRequired, e.Status,
        history.OrderBy(h => h.EventAt).ThenBy(h => h.Id)
            .Select(h => new ExecutionEventDto(h.EventType, h.EventAt, h.DelayReasonCode, h.Remarks, h.RecordedBy))
            .ToList());
}
