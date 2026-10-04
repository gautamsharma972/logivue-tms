using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Approvals.Domain;

/// <summary>
/// Lets <see cref="DelegateId"/> decide approvals that <see cref="DelegatorId"/> could decide, for a limited time
/// (leave cover). The delegate never gains permissions of their own; authority is re-checked on every decision.
/// </summary>
public sealed class Delegation : AggregateRoot, ITenantScoped
{
    private Delegation()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DelegatorId { get; private set; }

    public Guid DelegateId { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset ValidTo { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static Result<Delegation> Create(Guid tenantId, Guid delegatorId, Guid delegateId, DateTimeOffset from, DateTimeOffset to, string? reason, DateTimeOffset now)
    {
        if (delegatorId == delegateId)
        {
            return Error.Validation("approvals.delegate_self", "You cannot delegate to yourself.");
        }

        if (to <= from)
        {
            return Error.Validation("approvals.delegation_period", "The end of the delegation must be after its start.");
        }

        if (to <= now)
        {
            return Error.Validation("approvals.delegation_expired", "The delegation would already be over.");
        }

        return new Delegation
        {
            TenantId = tenantId,
            DelegatorId = delegatorId,
            DelegateId = delegateId,
            ValidFrom = from,
            ValidTo = to,
            Reason = reason?.Trim(),
        };
    }

    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ValidFrom <= now && now < ValidTo;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
