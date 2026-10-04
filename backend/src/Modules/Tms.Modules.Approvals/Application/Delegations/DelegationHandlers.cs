using Microsoft.EntityFrameworkCore;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Application.Delegations;

internal sealed class DelegationMapper(IUserDirectory directory, TimeProvider clock)
{
    public async Task<IReadOnlyList<DelegationDto>> ToDtosAsync(IReadOnlyCollection<Delegation> delegations, CancellationToken cancellationToken)
    {
        var names = await directory.GetDisplayNamesAsync(delegations.SelectMany(d => new[] { d.DelegatorId, d.DelegateId }), cancellationToken);
        var now = clock.GetUtcNow();
        string Name(Guid id) => names.TryGetValue(id, out var n) ? n : "Unknown user";

        return delegations
            .OrderByDescending(d => d.ValidFrom)
            .Select(d => new DelegationDto(d.Id, d.DelegatorId, Name(d.DelegatorId), d.DelegateId, Name(d.DelegateId),
                d.ValidFrom, d.ValidTo, d.Reason, d.IsActiveAt(now), d.Version))
            .ToList();
    }
}

internal sealed class ListDelegationsHandler(ApprovalsDbContext db, ICurrentUser currentUser, DelegationMapper mapper, TimeProvider clock)
{
    public async Task<Result<DelegationsDto>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Error.Unauthorized("approvals.no_user", "Sign in to view delegations.");
        }

        // Hide delegations that ended more than a month ago; the audit trail keeps the history.
        var cutoff = clock.GetUtcNow().AddDays(-30);
        var mine = await db.Delegations.AsNoTracking()
            .Where(d => (d.DelegatorId == me || d.DelegateId == me) && d.RevokedAt == null && d.ValidTo > cutoff)
            .ToListAsync(cancellationToken);

        var dtos = await mapper.ToDtosAsync(mine, cancellationToken);
        return new DelegationsDto(dtos.Where(d => d.DelegatorId == me).ToList(), dtos.Where(d => d.DelegateId == me).ToList());
    }
}

internal sealed class CreateDelegationHandler(
    ApprovalsDbContext db,
    ICurrentUser currentUser,
    IUserDirectory directory,
    DelegationMapper mapper,
    TimeProvider clock)
{
    public async Task<Result<DelegationDto>> HandleAsync(CreateDelegationRequest request, CancellationToken cancellationToken)
    {
        if (currentUser is not { UserId: { } me, TenantId: { } tenant })
        {
            return Error.Unauthorized("approvals.no_user", "Sign in to delegate approvals.");
        }

        var names = await directory.GetDisplayNamesAsync([request.DelegateId], cancellationToken);
        if (!names.ContainsKey(request.DelegateId))
        {
            return Error.Validation("approvals.delegate_unknown", "The selected person does not exist.");
        }

        var created = Delegation.Create(tenant, me, request.DelegateId, request.ValidFrom, request.ValidTo, request.Reason, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Delegations.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return (await mapper.ToDtosAsync([created.Value], cancellationToken))[0];
    }
}

internal sealed class RevokeDelegationHandler(ApprovalsDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var delegation = await db.Delegations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delegation is null || delegation.DelegatorId != currentUser.UserId)
        {
            return Error.NotFound("approvals.delegation_not_found", "Delegation not found.");
        }

        delegation.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
