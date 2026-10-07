using Microsoft.EntityFrameworkCore;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Application.Requests;

/// <summary>Entry point for other modules: starts an approval for one of their documents.</summary>
internal sealed class ApprovalGateway(ApprovalsDbContext db, ICurrentUser currentUser, DocumentTypeCatalog documentTypes, DecideRequestHandler decisions, TimeProvider clock)
    : IApprovalGateway
{
    public async Task<Result<ApprovalStatus>> DecideAsync(Guid requestId, bool approve, string? comment, CancellationToken cancellationToken = default)
    {
        var decided = approve
            ? await decisions.ApproveAsync(requestId, new DecisionRequest(comment), cancellationToken)
            : await decisions.RejectAsync(requestId, new DecisionRequest(comment), cancellationToken);
        return decided.IsFailure ? decided.Error : decided.Value.Status;
    }

    public async Task<Result<ApprovalSubmission>> SubmitAsync(SubmitApproval request, CancellationToken cancellationToken = default)
    {
        if (currentUser is not { UserId: { } requester, TenantId: { } tenant })
        {
            return Error.Unauthorized("approvals.no_user", "Approvals can only be submitted by a signed-in user.");
        }

        if (documentTypes.Find(request.DocumentType) is not { } type)
        {
            return Error.Validation("approvals.unknown_document_type", $"'{request.DocumentType}' is not a document type that supports approval.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300)
        {
            return Error.Validation("approvals.title_invalid", "A title of up to 300 characters is required.");
        }

        if (request.Amount is < 0)
        {
            return Error.Validation("approvals.amount_invalid", "The amount cannot be negative.");
        }

        var policy = await db.Policies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DocumentType == request.DocumentType && p.IsActive, cancellationToken);
        if (policy is null)
        {
            return Error.Conflict("approvals.no_policy", $"No active approval policy is configured for {type.Name}. Ask an administrator to set one up.");
        }

        var alreadyPending = await db.Requests.AnyAsync(
            r => r.DocumentType == request.DocumentType && r.DocumentId == request.DocumentId && r.Status == ApprovalStatus.Pending,
            cancellationToken);
        if (alreadyPending)
        {
            return Error.Conflict("approvals.already_pending", "This document already has an approval in progress.");
        }

        var approval = ApprovalRequest.Submit(
            tenant, request.DocumentType, request.DocumentId, request.Title, request.Amount, requester,
            policy.StepsFor(request.Amount), clock.GetUtcNow());

        db.Requests.Add(approval);
        await db.SaveChangesAsync(cancellationToken);
        return new ApprovalSubmission(approval.Id, approval.Status);
    }
}
