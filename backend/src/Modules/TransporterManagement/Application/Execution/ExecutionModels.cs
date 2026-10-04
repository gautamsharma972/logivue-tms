using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Execution;

public sealed record CreateExecutionRequest(string LoadReference, long TransporterId, bool PodRequired = true);

public sealed record RecordExecutionEventRequest(
    ExecutionEventType EventType,
    DateTime EventAt,
    string? DelayReasonCode = null,
    string? Remarks = null);

public sealed record ExecutionEventDto(
    ExecutionEventType EventType,
    DateTime EventAt,
    string? DelayReasonCode,
    string? Remarks,
    string RecordedBy);

/// <summary>
/// Planned and actual pickup and delivery for one load, with the stored delay and attribution for each.
/// Null delay minutes mean the event has not happened or the planned time is missing (Not Measurable).
/// </summary>
public sealed record LoadExecutionDto(
    long Id,
    string LoadReference,
    long TransporterId,
    long TenderId,
    DateTime? PlannedPickupAt,
    DateTime? ActualPickupAt,
    int? PickupDelayMinutes,
    string? PickupDelayReasonCode,
    DelayAttribution PickupAttribution,
    DateTime? PlannedDeliveryAt,
    DateTime? ActualDeliveryAt,
    int? DeliveryDelayMinutes,
    string? DeliveryDelayReasonCode,
    DelayAttribution DeliveryAttribution,
    bool PodRequired,
    ExecutionStatus Status,
    IReadOnlyList<ExecutionEventDto> Events);
