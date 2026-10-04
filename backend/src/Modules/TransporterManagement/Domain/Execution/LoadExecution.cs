using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Execution;

/// <summary>
/// The execution record of one accepted load. Planned times are copied from the accepted invitation; actual times
/// come from recorded events. Delay minutes and attribution are stored when the event is recorded, so history
/// does not change when the delay policy is later edited.
/// </summary>
public class LoadExecution
{
    public long Id { get; set; }
    public string LoadReference { get; set; } = string.Empty;
    public long TransporterId { get; set; }
    public long TenderId { get; set; }
    public DateTime? PlannedPickupAt { get; set; }
    public DateTime? ActualPickupAt { get; set; }
    public DateTime? PlannedDeliveryAt { get; set; }
    public DateTime? ActualDeliveryAt { get; set; }
    public int? PickupDelayMinutes { get; set; }
    public string? PickupDelayReasonCode { get; set; }
    public DelayAttribution PickupAttribution { get; set; } = DelayAttribution.None;
    public int? DeliveryDelayMinutes { get; set; }
    public string? DeliveryDelayReasonCode { get; set; }
    public DelayAttribution DeliveryAttribution { get; set; } = DelayAttribution.None;
    public bool PodRequired { get; set; } = true;
    public ExecutionStatus Status { get; set; } = ExecutionStatus.NotStarted;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LoadExecutionEvent
{
    public long Id { get; set; }
    public long LoadExecutionId { get; set; }
    public ExecutionEventType EventType { get; set; }
    public DateTime EventAt { get; set; }
    public string? DelayReasonCode { get; set; }
    public string? Remarks { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
}
