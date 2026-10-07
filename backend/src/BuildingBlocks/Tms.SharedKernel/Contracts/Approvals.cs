using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.SharedKernel.Contracts;

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
}

/// <summary>A kind of business document that can require approval (e.g. a spot rate, a freight bill).</summary>
public sealed record ApprovalDocumentType(string Code, string Name);

public sealed record SubmitApproval(string DocumentType, Guid DocumentId, string Title, decimal? Amount);

public sealed record ApprovalSubmission(Guid RequestId, ApprovalStatus Status);

/// <summary>
/// How business modules ask for approval. They never touch approval data directly: they submit through this gateway
/// and react to <see cref="ApprovalCompleted"/>.
/// </summary>
public interface IApprovalGateway
{
    Task<Result<ApprovalSubmission>> SubmitAsync(SubmitApproval request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides the current step of a request as the signed-in user, so a document module can offer "approve" and "reject" beside its own screens. The approval policy still decides
    /// who may: a user who is not an approver of the current step is refused.
    /// </summary>
    Task<Result<ApprovalStatus>> DecideAsync(Guid requestId, bool approve, string? comment, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised once, when a request reaches a final state. Also raised for requests that are auto-approved because
/// no policy step applied (e.g. amount below every threshold).
/// </summary>
public sealed record ApprovalCompleted(
    Guid RequestId,
    Guid TenantId,
    string DocumentType,
    Guid DocumentId,
    ApprovalStatus Outcome) : DomainEvent;
