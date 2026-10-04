using Microsoft.EntityFrameworkCore;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Application.Requests;

internal sealed class ListRequestsHandler(
    ApprovalsDbContext db,
    ApprovalAuthority authority,
    RequestMapper mapper,
    ICurrentUser currentUser)
{
    public async Task<Result<PagedResult<RequestSummaryDto>>> HandleAsync(ListRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Error.Unauthorized("approvals.no_user", "Sign in to view approvals.");
        }

        var context = await authority.LoadAsync(cancellationToken);
        var requests = db.Requests.AsNoTracking().AsQueryable();

        switch ((query.Scope ?? "inbox").ToLowerInvariant())
        {
            case "inbox":
                var permissions = context!.AllPermissions.ToList();
                var key = ApprovalRequest.KeyFor(me);
                requests = requests.Where(r =>
                    r.Status == ApprovalStatus.Pending
                    && r.CurrentPermission != null && permissions.Contains(r.CurrentPermission)
                    && r.RequesterId != me
                    && !r.DeciderKey.Contains(key));
                break;
            case "mine":
                requests = requests.Where(r => r.RequesterId == me);
                break;
            case "all":
                if (!currentUser.Permissions.Contains(ApprovalPermissions.ReadAll))
                {
                    return Error.Forbidden("approvals.forbidden", "You are not allowed to view every request.");
                }

                break;
            default:
                return Error.Validation("approvals.scope_invalid", "Scope must be inbox, mine or all.");
        }

        if (query.Status is { } status)
        {
            requests = requests.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.DocumentType))
        {
            requests = requests.Where(r => r.DocumentType == query.DocumentType);
        }

        // Oldest first in the inbox (work the queue in order); newest first everywhere else.
        var ordered = (query.Scope ?? "inbox").Equals("inbox", StringComparison.OrdinalIgnoreCase)
            ? requests.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            : requests.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);

        var page = await ordered.ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var names = await mapper.NamesAsync(page.Items, cancellationToken);

        return new PagedResult<RequestSummaryDto>(
            page.Items.Select(r => mapper.ToSummary(r, names, context)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetRequestHandler(ApprovalsDbContext db, ApprovalAuthority authority, RequestMapper mapper, ICurrentUser currentUser)
{
    public async Task<Result<RequestDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var context = await authority.LoadAsync(cancellationToken);
        if (request is null || context is null)
        {
            return Error.NotFound("approvals.not_found", "Approval request not found.");
        }

        var involved = request.RequesterId == context.UserId
            || request.HasDecided(context.UserId)
            || currentUser.Permissions.Contains(ApprovalPermissions.ReadAll)
            || request.Steps.Any(s => context.AllPermissions.Contains(s.RequiredPermission));

        // Same answer as "doesn't exist", so request ids can't be probed.
        if (!involved)
        {
            return Error.NotFound("approvals.not_found", "Approval request not found.");
        }

        var names = await mapper.NamesAsync([request], cancellationToken);
        return mapper.ToDto(request, names, context, CanCancel(request, context.UserId));
    }

    internal bool CanCancel(ApprovalRequest request, Guid me) =>
        request.Status == ApprovalStatus.Pending
        && (request.RequesterId == me || currentUser.Permissions.Contains(ApprovalPermissions.PoliciesManage));
}

internal sealed class DecideRequestHandler(
    ApprovalsDbContext db,
    ApprovalAuthority authority,
    RequestMapper mapper,
    TimeProvider clock)
{
    public Task<Result<RequestDto>> ApproveAsync(Guid id, DecisionRequest body, CancellationToken cancellationToken) =>
        DecideAsync(id, approve: true, body.Comment, cancellationToken);

    public Task<Result<RequestDto>> RejectAsync(Guid id, DecisionRequest body, CancellationToken cancellationToken) =>
        DecideAsync(id, approve: false, body.Comment, cancellationToken);

    private async Task<Result<RequestDto>> DecideAsync(Guid id, bool approve, string? comment, CancellationToken cancellationToken)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var context = await authority.LoadAsync(cancellationToken);
        if (request is null || context is null)
        {
            return Error.NotFound("approvals.not_found", "Approval request not found.");
        }

        var (allowed, onBehalfOf) = context.Resolve(request);
        if (!allowed)
        {
            // Give an accurate reason where it's safe to, otherwise a generic refusal.
            return request.Status != ApprovalStatus.Pending
                ? Error.Conflict("approvals.not_pending", "This request has already been completed.")
                : Error.Forbidden("approvals.not_authorised", "You are not authorised to decide the current step of this request.");
        }

        var now = clock.GetUtcNow();
        var outcome = approve
            ? request.Approve(context.UserId, onBehalfOf, comment, now)
            : request.Reject(context.UserId, onBehalfOf, comment, now);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var names = await mapper.NamesAsync([request], cancellationToken);
        return mapper.ToDto(request, names, context, canCancel: false);
    }
}

internal sealed class CancelRequestHandler(
    ApprovalsDbContext db,
    ApprovalAuthority authority,
    RequestMapper mapper,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<Result<RequestDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null || currentUser.UserId is not { } me)
        {
            return Error.NotFound("approvals.not_found", "Approval request not found.");
        }

        if (request.RequesterId != me && !currentUser.Permissions.Contains(ApprovalPermissions.PoliciesManage))
        {
            return Error.Forbidden("approvals.not_authorised", "Only the requester or an approvals administrator can cancel a request.");
        }

        var cancelled = request.Cancel(clock.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var names = await mapper.NamesAsync([request], cancellationToken);
        return mapper.ToDto(request, names, await authority.LoadAsync(cancellationToken), canCancel: false);
    }
}
