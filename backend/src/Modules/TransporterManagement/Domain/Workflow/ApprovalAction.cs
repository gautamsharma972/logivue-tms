using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Workflow;

/// <summary>
/// One approval or status-change action. Captures who, what, when, and why, and is never edited.
/// </summary>
public class TransporterApprovalAction
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public ApprovalActionType Action { get; set; }
    public TransporterStatus FromStatus { get; set; }
    public TransporterStatus ToStatus { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public DateTime ActionAt { get; set; }
    public string? Comments { get; set; }
    public string? Reason { get; set; }
}
