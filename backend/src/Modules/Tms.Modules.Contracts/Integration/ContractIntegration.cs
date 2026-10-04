using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Contracts.Integration;

/// <summary>Reacts to the approval engine: activates / rejects a contract when its approval is decided, and retires the contract a revision replaces.</summary>
internal sealed class ContractApprovalSubscriber(ContractsDbContext db, TimeProvider clock) : IDomainEventHandler<ApprovalCompleted>
{
    public async Task HandleAsync(ApprovalCompleted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.DocumentType != SubmitContractHandler.DocumentType)
        {
            return;
        }

        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == domainEvent.DocumentId, cancellationToken);
        if (contract is null || !contract.ApplyApprovalOutcome(domainEvent.RequestId, domainEvent.Outcome, clock.GetUtcNow()))
        {
            return;
        }

        await db.SwitchOverAsync(contract, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
