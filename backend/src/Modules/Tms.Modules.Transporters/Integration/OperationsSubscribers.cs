using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Application.Operations;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Application.Settings;
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

internal sealed class ShipmentAcceptedSubscriber(
    TransportersDbContext db, ExecutionService executions, IShipmentOperationsFeed feed, ITransporterSettings settings, PerformanceEngine engine) : IDomainEventHandler<ShipmentAccepted>
{
    public async Task HandleAsync(ShipmentAccepted e, CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.Where(i => i.ShipmentId == e.ShipmentId && i.TransporterId == e.TransporterId && i.Outcome == InvitationOutcome.Open)
            .OrderByDescending(i => i.SentAt).FirstOrDefaultAsync(cancellationToken);
        invitation?.Respond(InvitationOutcome.Accepted, e.AcceptedAt, null);

        var execution = await executions.EnsureAsync(e.ShipmentId, cancellationToken);

        // The vehicle named at acceptance is expected at the pickup a little before the load is due to leave.
        var anchors = new List<DateTimeOffset> { e.AcceptedAt };
        if (!await db.Placements.AnyAsync(p => p.ShipmentId == e.ShipmentId, cancellationToken) && await feed.GetAsync(e.ShipmentId, cancellationToken) is { } fact)
        {
            var planned = ExecutionService.PlannedTimes(fact, await settings.GetAsync<PlannedTimesSetting>(SettingKeys.PlannedTimes, cancellationToken)).Pickup;
            var lead = await settings.GetAsync<int>(SettingKeys.PlacementLeadMinutes, cancellationToken);
            var required = (planned ?? e.AcceptedAt).AddMinutes(-lead);
            db.Placements.Add(VehiclePlacement.Create(e.TenantId, fact, fact.VehicleId, fact.VehicleRegistration, required, e.AcceptedAt));
            anchors.Add(required);
        }

        await db.SaveChangesAsync(cancellationToken);

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

/// <summary>A swap of vehicle before departure counts as a vehicle replacement against the transporter.</summary>
internal sealed class ShipmentVehicleReassignedSubscriber(TransportersDbContext db, PerformanceEngine engine) : IDomainEventHandler<ShipmentVehicleReassigned>
{
    public async Task HandleAsync(ShipmentVehicleReassigned e, CancellationToken cancellationToken)
    {
        var placement = await db.Placements.Include(p => p.Events).FirstOrDefaultAsync(p => p.ShipmentId == e.ShipmentId, cancellationToken);
        if (placement is null || !placement.Replace(e.VehicleId, e.Registration, e.OccurredAt))
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(e.TransporterId, [placement.RequiredAt], cancellationToken);
    }
}

/// <summary>A cancelled shipment closes its placement and its execution, so nothing is measured or chased for a load that will not move.</summary>
internal sealed class ShipmentCancelledSubscriber(TransportersDbContext db, AlertService alerts, PerformanceEngine engine) : IDomainEventHandler<ShipmentCancelled>
{
    public async Task HandleAsync(ShipmentCancelled e, CancellationToken cancellationToken)
    {
        var anchors = new List<DateTimeOffset>();
        var placement = await db.Placements.Include(p => p.Events).FirstOrDefaultAsync(p => p.ShipmentId == e.ShipmentId, cancellationToken);
        if (placement is not null && placement.Cancel($"Shipment cancelled: {e.Reason}", e.OccurredAt).IsSuccess)
        {
            await alerts.ResolveAsync($"placement:{placement.Id:N}", ["PLACEMENT_OVERDUE"], cancellationToken);
            anchors.Add(placement.RequiredAt);
        }

        var execution = await db.Executions.FirstOrDefaultAsync(x => x.ShipmentId == e.ShipmentId, cancellationToken);
        if (execution is not null && execution.Status != ExecutionStatus.Delivered)
        {
            execution.Cancel();
            await alerts.ResolveAsync($"exec:{execution.Id:N}:pickup-overdue", ["PICKUP_OVERDUE"], cancellationToken);
            await alerts.ResolveAsync($"exec:{execution.Id:N}:delivery-overdue", ["DELIVERY_OVERDUE"], cancellationToken);
            anchors.AddRange(new[] { execution.PlannedPickupAt, execution.PlannedDeliveryAt }.Where(d => d is not null).Select(d => d!.Value));
        }

        if (anchors.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await engine.RefreshAsync(e.TransporterId, anchors, cancellationToken);
        }
    }
}

/// <summary>Goods that arrive short or damaged start a claim against the transporter. The value is left for the person handling it to fill in.</summary>
internal sealed class DeliveryExceptionSubscriber(TransportersDbContext db, TimeProvider clock, PerformanceEngine engine) : IDomainEventHandler<DeliveryExceptionReported>
{
    public async Task HandleAsync(DeliveryExceptionReported e, CancellationToken cancellationToken)
    {
        var key = $"exception:{e.ShipmentId:N}:{e.OrderId:N}";
        if (await db.Claims.AnyAsync(c => c.SourceKey == key, cancellationToken))
        {
            return;
        }

        var type = e.DamagedPackages > 0 ? ClaimType.Damage : ClaimType.Shortage;
        var detail = $"{e.OrderNumber}: {(e.DamagedPackages > 0 ? $"{e.DamagedPackages} package(s) damaged" : string.Empty)}{(e.DamagedPackages > 0 && e.ShortagePackages > 0 ? ", " : string.Empty)}{(e.ShortagePackages > 0 ? $"{e.ShortagePackages} package(s) short" : string.Empty)}.{(string.IsNullOrWhiteSpace(e.Remarks) ? string.Empty : " " + e.Remarks)}";
        var claim = ClaimRecord.Create(e.TenantId, e.TransporterId, e.ShipmentId, e.ShipmentNumber, type, DateOnly.FromDateTime(e.ReportedAt.ToOffset(TimeSpan.FromMinutes(330)).DateTime), 0m, "Raised from the delivery. " + detail, clock.TodayInIndia(), key);
        if (claim.IsFailure)
        {
            return;
        }

        db.Claims.Add(claim.Value);
        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(e.TransporterId, [e.ReportedAt], cancellationToken);
    }
}
