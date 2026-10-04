using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Approvals.Domain;

public enum StepStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <param name="OnBehalfOf">Set when the decision was made by a delegate; the person whose authority was used.</param>
public sealed record RequestStep(
    int Order,
    string Name,
    string RequiredPermission,
    StepStatus Status,
    Guid? DecidedBy,
    Guid? OnBehalfOf,
    DateTimeOffset? DecidedAt,
    string? Comment);

/// <summary>A document waiting for (or finished with) approval. Steps run strictly in order.</summary>
public sealed class ApprovalRequest : AggregateRoot, ITenantScoped
{
    private ApprovalRequest()
    {
    }

    public Guid TenantId { get; private set; }

    public string DocumentType { get; private set; } = null!;

    public Guid DocumentId { get; private set; }

    public string Title { get; private set; } = null!;

    public decimal? Amount { get; private set; }

    public Guid RequesterId { get; private set; }

    public ApprovalStatus Status { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<RequestStep> Steps { get; private set; } = [];

    /// <summary>Index into <see cref="Steps"/> of the step awaiting a decision; null once finished.</summary>
    public int? CurrentStepIndex { get; private set; }

    /// <summary>Denormalised permission of the current step so "my inbox" can be queried in SQL.</summary>
    public string? CurrentPermission { get; private set; }

    /// <summary>Denormalised <c>|id|id|</c> list of everyone who has decided a step, queryable with LIKE.</summary>
    public string DeciderKey { get; private set; } = string.Empty;

    public RequestStep? CurrentStep => CurrentStepIndex is { } i ? Steps[i] : null;

    public static string KeyFor(Guid userId) => $"|{userId}|";

    public bool HasDecided(Guid userId) => DeciderKey.Contains(KeyFor(userId), StringComparison.Ordinal);

    public static ApprovalRequest Submit(
        Guid tenantId,
        string documentType,
        Guid documentId,
        string title,
        decimal? amount,
        Guid requesterId,
        IReadOnlyList<PolicyStep> applicableSteps,
        DateTimeOffset now)
    {
        var request = new ApprovalRequest
        {
            TenantId = tenantId,
            DocumentType = documentType,
            DocumentId = documentId,
            Title = title.Trim(),
            Amount = amount,
            RequesterId = requesterId,
            Steps = applicableSteps
                .Select((s, i) => new RequestStep(i + 1, s.Name, s.RequiredPermission, StepStatus.Pending, null, null, null, null))
                .ToList(),
        };

        if (request.Steps.Count == 0)
        {
            request.Finish(ApprovalStatus.Approved, now); // no step applies to this amount: nothing to wait for
        }
        else
        {
            request.Status = ApprovalStatus.Pending;
            request.MoveTo(0);
        }

        return request;
    }

    public Result Approve(Guid userId, Guid? onBehalfOf, string? comment, DateTimeOffset now)
    {
        if (Check(userId, onBehalfOf) is { } error)
        {
            return error;
        }

        Decide(userId, onBehalfOf, StepStatus.Approved, comment, now);
        if (CurrentStepIndex is { } i && i + 1 < Steps.Count)
        {
            MoveTo(i + 1);
        }
        else
        {
            Finish(ApprovalStatus.Approved, now);
        }

        return Result.Success();
    }

    public Result Reject(Guid userId, Guid? onBehalfOf, string? comment, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return Error.Validation("approvals.comment_required", "Please say why the request is rejected.");
        }

        if (Check(userId, onBehalfOf) is { } error)
        {
            return error;
        }

        Decide(userId, onBehalfOf, StepStatus.Rejected, comment, now);
        Finish(ApprovalStatus.Rejected, now);
        return Result.Success();
    }

    /// <summary>Withdraws a pending request. Who may cancel is decided by the caller (requester or an administrator).</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
        {
            return Error.Conflict("approvals.not_pending", "Only a pending request can be cancelled.");
        }

        Finish(ApprovalStatus.Cancelled, now);
        return Result.Success();
    }

    private Error? Check(Guid userId, Guid? onBehalfOf)
    {
        if (Status != ApprovalStatus.Pending || CurrentStepIndex is null)
        {
            return Error.Conflict("approvals.not_pending", "This request has already been completed.");
        }

        var actors = onBehalfOf is { } d ? new[] { userId, d } : [userId];
        if (actors.Contains(RequesterId))
        {
            return Error.Forbidden("approvals.self_approval", "You cannot decide a request you submitted.");
        }

        if (actors.Any(HasDecided))
        {
            return Error.Forbidden("approvals.already_decided", "You have already decided an earlier step of this request.");
        }

        return null;
    }

    private void Decide(Guid userId, Guid? onBehalfOf, StepStatus status, string? comment, DateTimeOffset now)
    {
        var index = CurrentStepIndex!.Value;
        var steps = Steps.ToList();
        steps[index] = steps[index] with
        {
            Status = status,
            DecidedBy = userId,
            OnBehalfOf = onBehalfOf,
            DecidedAt = now,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        };
        Steps = steps;

        DeciderKey += DeciderKey.Length == 0 ? KeyFor(userId) : $"{userId}|";
        if (onBehalfOf is { } d)
        {
            DeciderKey += $"{d}|";
        }
    }

    private void MoveTo(int index)
    {
        CurrentStepIndex = index;
        CurrentPermission = Steps[index].RequiredPermission;
    }

    private void Finish(ApprovalStatus outcome, DateTimeOffset now)
    {
        Status = outcome;
        CompletedAt = now;
        CurrentStepIndex = null;
        CurrentPermission = null;
        Raise(new ApprovalCompleted(Id, TenantId, DocumentType, DocumentId, outcome));
    }
}
