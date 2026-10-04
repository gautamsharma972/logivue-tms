using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Pod;

/// <summary>
/// Proof of delivery for one delivered load. Created when delivery completes (when POD is required), and tracked
/// through its review lifecycle. A dedicated POD module can replace this later behind ITransporterPodProvider.
/// </summary>
public class PodRecord
{
    public long Id { get; set; }
    public string LoadReference { get; set; } = string.Empty;
    public long TransporterId { get; set; }
    public long LoadExecutionId { get; set; }
    public DateTime DeliveredAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public bool? SubmittedWithinSla { get; set; }
    public DateOnly? PodDate { get; set; }
    public string? ReceivedBy { get; set; }
    public string? FileReference { get; set; }
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public PodStatus Status { get; set; } = PodStatus.Pending;
    public int RejectionCount { get; set; }
    public string? RejectionReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
