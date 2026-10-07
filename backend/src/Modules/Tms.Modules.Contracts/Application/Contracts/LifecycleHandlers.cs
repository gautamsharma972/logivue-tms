using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Contracts;

internal sealed class SuspendContractHandler(ContractsDbContext db, ContractLoader loader, TimeProvider clock)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, ContractReasonRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var suspended = found.Value.Suspend(request.Reason, clock.TodayInIndia());
        if (suspended.IsFailure)
        {
            return suspended.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

internal sealed class ResumeContractHandler(ContractsDbContext db, ContractLoader loader, TimeProvider clock)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var resumed = found.Value.Resume(clock.TodayInIndia());
        if (resumed.IsFailure)
        {
            return resumed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

internal sealed class CancelContractHandler(ContractsDbContext db, ContractLoader loader)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, ContractReasonRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var cancelled = found.Value.Cancel(request.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

/// <summary>Starts the next term as a draft copy of this contract, optionally with every rate moved by a percentage. Nothing is in force until the draft is approved.</summary>
internal sealed class RenewContractHandler(ContractsDbContext db, ContractLoader loader, ICurrentUser currentUser)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, RenewRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        if (await db.Contracts.AnyAsync(c => c.RevisionOfId == id && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.PendingApproval || c.Status == ContractStatus.Rejected), cancellationToken))
        {
            return Error.Conflict("contracts.revision_in_progress", "A revision or renewal of this contract is already being prepared.");
        }

        if (request.UpliftPercent is < -90 or > 500)
        {
            return Error.Validation("contracts.uplift_invalid", "Move rates by between -90% and +500%.");
        }

        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        await db.Entry(contract).Collection(c => c.Accessorials).LoadAsync(cancellationToken);
        await db.Entry(contract).Collection(c => c.Capacities).LoadAsync(cancellationToken);
        await db.Entry(contract).Collection(c => c.Slas).LoadAsync(cancellationToken);

        var from = request.EffectiveFrom ?? contract.EffectiveTo.AddDays(1);
        var length = contract.EffectiveTo.DayNumber - contract.EffectiveFrom.DayNumber;
        var to = request.EffectiveTo ?? from.AddDays(Math.Max(length, 30));
        var created = contract.CreateRevision(from, to, currentUser.UserId, RevisionKind.Renewal);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var renewal = created.Value;
        if (request.UpliftPercent is { } pct and not 0)
        {
            var factor = 1m + (pct / 100m);
            var scaled = contract.RateCards.Select(c => c.ToSpec() with
            {
                Pricing = c.Pricing.Scale(factor),
                Extras = c.Extras with
                {
                    MinimumCharge = c.MinimumCharge is { } min ? Math.Round(min * factor, 2, MidpointRounding.AwayFromZero) : null,
                    MaximumCharge = c.MaximumCharge is { } max ? Math.Round(max * factor, 2, MidpointRounding.AwayFromZero) : null,
                },
            }).ToList();
            var replaced = renewal.ReplaceRates(scaled, contract.RateCards.ToDictionary(c => c.Code));
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }
        }

        db.Contracts.Add(renewal);
        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(renewal, cancellationToken);
    }
}

/// <summary>Approves or rejects a contract's pending approval from the contract's own screen. The approval policy still decides who is allowed to.</summary>
internal sealed class DecideContractHandler(ContractsDbContext db, ContractLoader loader, ContractAccess access, IApprovalGateway approvals)
{
    public Task<Result<ContractDto>> ApproveAsync(Guid id, DecisionBody body, CancellationToken cancellationToken) => DecideAsync(id, true, body.Comment, cancellationToken);

    public Task<Result<ContractDto>> RejectAsync(Guid id, DecisionBody body, CancellationToken cancellationToken) => DecideAsync(id, false, body.Comment, cancellationToken);

    private async Task<Result<ContractDto>> DecideAsync(Guid id, bool approve, string? comment, CancellationToken cancellationToken)
    {
        if (!access.CanDecide)
        {
            return ContractAccess.Forbidden;
        }

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contract is null)
        {
            return ContractAccess.NotFound;
        }

        if (contract.Status != ContractStatus.PendingApproval || contract.ApprovalRequestId is not { } requestId)
        {
            return Error.Conflict("contracts.not_pending", "This contract is not waiting for a decision.");
        }

        if (!approve && string.IsNullOrWhiteSpace(comment))
        {
            return Error.Validation("contracts.reason_required", "Say why the contract is being rejected.");
        }

        var decided = await approvals.DecideAsync(requestId, approve, comment, cancellationToken);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        // The approval's completion event has activated or rejected the contract; read what it is now.
        await db.Entry(contract).ReloadAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);
    }
}
