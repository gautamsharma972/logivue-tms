using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Transporters.Integration;

/// <summary>
/// Turns what happens to a shipment into performance records: who was offered it and what they answered, and when it left and arrived. Every
/// handler is idempotent (a re-delivered event changes nothing), as the outbox may deliver an event more than once.
/// </summary>
internal sealed class ShipmentTenderedSubscriber(TransportersDbContext db, IShipmentOperationsFeed feed) : IDomainEventHandler<ShipmentTendered>
{
    public async Task HandleAsync(ShipmentTendered e, CancellationToken cancellationToken)
    {
        if (await db.Invitations.AnyAsync(i => i.ShipmentId == e.ShipmentId && i.SentAt == e.TenderedAt, cancellationToken))
        {
            return;
        }

        var fact = await feed.GetAsync(e.ShipmentId, cancellationToken);
        db.Invitations.Add(TenderInvitation.Create(e.TenantId, fact, e.ShipmentId, e.Number, e.TransporterId, e.TenderedAt));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class ShipmentAcceptedSubscriber(TransportersDbContext db, ExecutionService executions, PerformanceEngine engine) : IDomainEventHandler<ShipmentAccepted>
{
    public async Task HandleAsync(ShipmentAccepted e, CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.Where(i => i.ShipmentId == e.ShipmentId && i.TransporterId == e.TransporterId && i.Outcome == InvitationOutcome.Open)
            .OrderByDescending(i => i.SentAt).FirstOrDefaultAsync(cancellationToken);
        invitation?.Respond(InvitationOutcome.Accepted, e.AcceptedAt, null);

        var execution = await executions.EnsureAsync(e.ShipmentId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var anchors = new List<DateTimeOffset> { e.AcceptedAt };
        if (invitation is not null)
        {
            anchors.Add(invitation.SentAt);
        }

        if (execution is not null)
        {
            anchors.AddRange(ExecutionService.Anchors(execution, e.AcceptedAt));
        }

        await engine.RefreshAsync(e.TransporterId, anchors, cancellationToken);
    }
}

internal sealed class ShipmentRejectedSubscriber(TransportersDbContext db, PerformanceEngine engine) : IDomainEventHandler<ShipmentRejected>
{
    public async Task HandleAsync(ShipmentRejected e, CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.Where(i => i.ShipmentId == e.ShipmentId && i.TransporterId == e.TransporterId && i.Outcome == InvitationOutcome.Open)
            .OrderByDescending(i => i.SentAt).FirstOrDefaultAsync(cancellationToken);
        if (invitation is null || !invitation.Respond(InvitationOutcome.Rejected, e.RejectedAt, e.Reason))
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(e.TransporterId, [invitation.SentAt, e.RejectedAt], cancellationToken);
    }
}

internal sealed class ShipmentDispatchedSubscriber(ExecutionService executions) : IDomainEventHandler<ShipmentDispatched>
{
    public async Task HandleAsync(ShipmentDispatched e, CancellationToken cancellationToken)
    {
        var execution = await executions.EnsureAsync(e.ShipmentId, cancellationToken);
        if (execution is null || execution.Has(ExecutionEventType.VehicleDeparture))
        {
            return;
        }

        // The time is recorded by the system; whether the carrier was to blame is for a person to say afterwards (a late departure is "unattributed" until then).
        await executions.RecordAsync(execution, ExecutionEventType.VehicleDeparture, e.DispatchedAt, null, "Recorded when the shipment was dispatched.", cancellationToken);
    }
}

internal sealed class ShipmentDeliveredSubscriber(ExecutionService executions) : IDomainEventHandler<ShipmentDelivered>
{
    public async Task HandleAsync(ShipmentDelivered e, CancellationToken cancellationToken)
    {
        var execution = await executions.EnsureAsync(e.ShipmentId, cancellationToken);
        if (execution is null || execution.Has(ExecutionEventType.DeliveryComplete))
        {
            return;
        }

        await executions.RecordAsync(execution, ExecutionEventType.DeliveryComplete, e.DeliveredAt, null, "Recorded when the shipment was delivered.", cancellationToken);
    }
}
