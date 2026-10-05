using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Transporters.Integration;

/// <summary>Reads the delivery module's events into one record per delivery. Every handler is idempotent.</summary>
internal sealed class ProofPerformanceRecorder(TransportersDbContext db, TimeProvider clock)
{
    public async Task<ProofPerformance?> RecordAsync(Guid tenantId, Guid deliveryId, Guid? transporterId, string number, CancellationToken cancellationToken)
    {
        if (transporterId is not { } carrier)
        {
            return null; // nobody to hold it against
        }

        var row = db.ProofPerformances.Local.FirstOrDefault(p => p.DeliveryId == deliveryId) ?? await db.ProofPerformances.FirstOrDefaultAsync(p => p.DeliveryId == deliveryId, cancellationToken);
        if (row is null)
        {
            row = ProofPerformance.Open(tenantId, deliveryId, carrier, number, clock.GetUtcNow());
            db.ProofPerformances.Add(row);
        }

        return row;
    }

    public DateTimeOffset Now => clock.GetUtcNow();
}

internal sealed class DeliveryCompletedPerformanceSubscriber(ProofPerformanceRecorder recorder, TransportersDbContext db) : IDomainEventHandler<DeliveryCompleted>
{
    public async Task HandleAsync(DeliveryCompleted e, CancellationToken cancellationToken)
    {
        if (await recorder.RecordAsync(e.TenantId, e.DeliveryId, e.TransporterId, e.Number, cancellationToken) is { } row)
        {
            row.Delivered(e.DeliveredAt, e.OnTime, e.ShortQuantity, e.DamagedQuantity, recorder.Now);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal sealed class PodAcceptedPerformanceSubscriber(ProofPerformanceRecorder recorder, TransportersDbContext db) : IDomainEventHandler<PodAccepted>
{
    public async Task HandleAsync(PodAccepted e, CancellationToken cancellationToken)
    {
        if (await recorder.RecordAsync(e.TenantId, e.DeliveryId, e.TransporterId, e.DeliveryNumber, cancellationToken) is { } row)
        {
            row.Accepted(e.AcceptedAt, e.AcceptedFirstTime, e.SubmittedWithinSla, e.DeliveredOnTime, e.ShortQuantity, e.DamagedQuantity, e.DeliveredAt, recorder.Now);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal sealed class PodRejectedPerformanceSubscriber(ProofPerformanceRecorder recorder, TransportersDbContext db) : IDomainEventHandler<PodRejected>
{
    public async Task HandleAsync(PodRejected e, CancellationToken cancellationToken)
    {
        if (await recorder.RecordAsync(e.TenantId, e.DeliveryId, e.TransporterId, e.DeliveryNumber, cancellationToken) is { } row)
        {
            row.Rejected(e.RejectedAt, recorder.Now);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal sealed class DeliveryExceptionPerformanceSubscriber(ProofPerformanceRecorder recorder, TransportersDbContext db) : IDomainEventHandler<DeliveryExceptionRaised>
{
    public async Task HandleAsync(DeliveryExceptionRaised e, CancellationToken cancellationToken)
    {
        if (e.ExceptionType is not ("CustomerRefusal" or "DeliveryFailed" or "AddressIssue"))
        {
            return;
        }

        if (await recorder.RecordAsync(e.TenantId, e.DeliveryId, e.TransporterId, e.DeliveryNumber, cancellationToken) is { } row)
        {
            if (e.ExceptionType == "CustomerRefusal")
            {
                row.MarkRefused(recorder.Now);
            }
            else
            {
                row.MarkFailed(recorder.Now);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
