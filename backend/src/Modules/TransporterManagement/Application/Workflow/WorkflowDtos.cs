using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Workflow;

/// <summary>Optional free-text comment for actions that do not need a reason.</summary>
public sealed record CommentsRequest(string? Comments);

/// <summary>Mandatory reason for actions that end or restrict a transporter's participation.</summary>
public sealed record ReasonRequest(string Reason, string? Comments = null);

public sealed record ApprovalActionDto(
    long Id,
    ApprovalActionType Action,
    TransporterStatus FromStatus,
    TransporterStatus ToStatus,
    string ActorUserId,
    DateTime ActionAt,
    string? Comments,
    string? Reason);

public sealed record OnboardingStepDto(long Id, int Sequence, TransporterStatus Status, string Name, string RequiredRole);
