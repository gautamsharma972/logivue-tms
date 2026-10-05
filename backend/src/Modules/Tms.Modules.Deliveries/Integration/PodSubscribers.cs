using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Claims;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Deliveries.Integration;

/// <summary>Tells freight audit where a delivery's proof stands, and (when the tenant asks) raises the claims. Each handler is idempotent: a re-delivered event changes nothing.</summary>
internal sealed class FreightAuditReporter(DeliveriesDbContext db, IFreightAuditIntegration audit, IDeliverySettings settings, TimeProvider clock)
{
    public async Task ReportAsync(Guid deliveryId, Guid? podId, string status, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return;
        }

        var hold = (await settings.GetAsync<BillingSetting>(DeliverySettingKeys.Billing, cancellationToken)).HoldInvoiceUntilAccepted;
        var eligible = status == "Accepted" || (!hold && status != "Rejected");
        await audit.NotifyPodStatusChangedAsync(
            new PodStatusChange(delivery.Id, podId, delivery.Number, delivery.ShipmentId, delivery.ShipmentReference, delivery.TransporterId, status, eligible, !eligible, clock.GetUtcNow()), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class DeliveryCompletedSubscriber(FreightAuditReporter reporter) : IDomainEventHandler<DeliveryCompleted>
{
    public Task HandleAsync(DeliveryCompleted e, CancellationToken cancellationToken) => reporter.ReportAsync(e.DeliveryId, null, "Pending", cancellationToken);
}

internal sealed class PodSubmittedSubscriber(FreightAuditReporter reporter) : IDomainEventHandler<PodSubmitted>
{
    public Task HandleAsync(PodSubmitted e, CancellationToken cancellationToken) => reporter.ReportAsync(e.DeliveryId, e.PodId, "Submitted", cancellationToken);
}

internal sealed class PodRejectedSubscriber(FreightAuditReporter reporter) : IDomainEventHandler<PodRejected>
{
    public Task HandleAsync(PodRejected e, CancellationToken cancellationToken) => reporter.ReportAsync(e.DeliveryId, e.PodId, "Rejected", cancellationToken);
}

internal sealed class PodAcceptedSubscriber(FreightAuditReporter reporter, DeliveriesDbContext db, ClaimService claims, IDeliverySettings settings) : IDomainEventHandler<PodAccepted>
{
    public async Task HandleAsync(PodAccepted e, CancellationToken cancellationToken)
    {
        await reporter.ReportAsync(e.DeliveryId, e.PodId, "Accepted", cancellationToken);

        if (!(await settings.GetAsync<DiscrepancyRulesSetting>(DeliverySettingKeys.Discrepancy, cancellationToken)).AutoCreateClaim)
        {
            return;
        }

        var delivery = await db.Deliveries.Include(d => d.Items).Include(d => d.Discrepancies).AsSplitQuery().FirstOrDefaultAsync(d => d.Id == e.DeliveryId, cancellationToken);
        if (delivery is not null && (await claims.CreateAsync(delivery, null, cancellationToken)).Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
