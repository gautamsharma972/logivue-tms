using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.TransportOps.Integration;

/// <summary>An approval decision on a customer invoice. Idempotent: a repeated or unrelated notice changes nothing.</summary>
internal sealed class InvoiceApprovalSubscriber(TransportOpsDbContext db, TimeProvider clock) : IDomainEventHandler<ApprovalCompleted>
{
    public async Task HandleAsync(ApprovalCompleted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.DocumentType != InvoiceHandler.DocumentType)
        {
            return;
        }

        var invoice = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == domainEvent.DocumentId, cancellationToken);
        if (invoice is null || !invoice.ApplyApprovalOutcome(domainEvent.RequestId, domainEvent.Outcome == ApprovalStatus.Approved, clock.GetUtcNow()))
        {
            return;
        }

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            // Not approved: the deliveries become available to invoice again.
            var calcIds = invoice.Lines.Select(l => l.FreightCalculationId).ToList();
            foreach (var calc in await db.Calculations.Where(c => calcIds.Contains(c.Id)).ToListAsync(cancellationToken))
            {
                calc.ReleaseFromInvoice();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
