using Microsoft.EntityFrameworkCore;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Application;

/// <summary>Who the caller is, what they hold, and whose authority they currently borrow through delegations.</summary>
internal sealed class AuthorityContext(Guid userId, IReadOnlySet<string> own, IReadOnlyDictionary<Guid, IReadOnlySet<string>> delegated)
{
    public Guid UserId { get; } = userId;

    public IReadOnlySet<string> Own { get; } = own;

    /// <summary>Delegator id → the permissions that delegator holds.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlySet<string>> Delegated { get; } = delegated;

    public IReadOnlySet<string> AllPermissions =>
        Own.Concat(Delegated.Values.SelectMany(p => p)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Returns who the caller would act as for the request's current step: themselves (<c>Self</c>) or a delegator,
    /// or null when they have no authority. Segregation of duties is applied here and again in the domain.
    /// </summary>
    public (bool Allowed, Guid? OnBehalfOf) Resolve(ApprovalRequest request)
    {
        if (request.Status != ApprovalStatus.Pending || request.CurrentStep is not { } step)
        {
            return (false, null);
        }

        if (UserId == request.RequesterId || request.HasDecided(UserId))
        {
            return (false, null);
        }

        if (Own.Contains(step.RequiredPermission))
        {
            return (true, null);
        }

        foreach (var (delegator, permissions) in Delegated)
        {
            if (delegator != request.RequesterId && !request.HasDecided(delegator) && permissions.Contains(step.RequiredPermission))
            {
                return (true, delegator);
            }
        }

        return (false, null);
    }
}

internal sealed class ApprovalAuthority(ApprovalsDbContext db, IUserDirectory directory, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<AuthorityContext?> LoadAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var delegators = await db.Delegations.AsNoTracking()
            .Where(d => d.DelegateId == me && d.RevokedAt == null && d.ValidFrom <= now && d.ValidTo > now)
            .Select(d => d.DelegatorId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var delegated = new Dictionary<Guid, IReadOnlySet<string>>();
        foreach (var delegator in delegators)
        {
            delegated[delegator] = await directory.GetPermissionsAsync(delegator, cancellationToken);
        }

        return new AuthorityContext(me, currentUser.Permissions, delegated);
    }
}
