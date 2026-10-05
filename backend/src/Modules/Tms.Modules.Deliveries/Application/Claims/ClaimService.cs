using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Claims;

public sealed record CreateClaimsRequest(IReadOnlyList<Guid>? DiscrepancyIds);

public sealed record ClaimResultDto(Guid DiscrepancyId, string Sku, DiscrepancyType Type, decimal Quantity, string Reference, string System);

public sealed record BillingStatusDto(string Status, bool BillingEligible, bool InvoiceHold, DateTimeOffset? At, string Explanation);

/// <summary>Turns what was found at the drop into a claim request that already carries everything: the load, the goods, the people, the evidence. Nothing is retyped.</summary>
internal sealed class ClaimService(DeliveriesDbContext db, IClaimsIntegration claims)
{
    public async Task<IReadOnlyList<ClaimResultDto>> CreateAsync(Delivery delivery, IReadOnlyCollection<Guid>? only, CancellationToken cancellationToken)
    {
        var pod = await db.Pods.AsNoTracking().Include(p => p.Evidence).Where(p => p.DeliveryId == delivery.Id && p.IsCurrent).FirstOrDefaultAsync(cancellationToken);
        var evidence = pod?.Evidence.Where(e => e.IsActive).Select(e => e.Id).ToList() ?? [];
        var items = delivery.Items.ToDictionary(i => i.Id);
        var open = await db.Exceptions.Where(e => e.DeliveryId == delivery.Id && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed && e.ClaimReference == null).ToListAsync(cancellationToken);

        var results = new List<ClaimResultDto>();
        foreach (var d in delivery.Discrepancies.Where(d => d.ClaimReference is null && (only is null || only.Contains(d.Id))))
        {
            var item = items.GetValueOrDefault(d.DeliveryItemId);
            var request = new ClaimRequest(
                delivery.Id, delivery.Number, delivery.ShipmentId, delivery.ShipmentReference, delivery.CustomerName, delivery.CustomerReference, delivery.TransporterId, delivery.TransporterReference,
                delivery.VehicleReference, item?.SkuReference ?? "?", item?.Description ?? string.Empty, d.Type.ToString(), d.Quantity, d.ReasonCode, d.Description, pod?.Id, pod?.PodNumber, evidence,
                d.CustomerAcknowledged || pod?.CustomerAcknowledged == true, pod?.RecipientName, pod?.Latitude, pod?.Longitude, delivery.ActualDeliveryAt ?? d.CreatedAt, pod?.DriverRemarks);
            var reference = await claims.CreateClaimAsync(request, cancellationToken);
            d.LinkClaim(reference.Reference);
            var kinds = d.Type switch
            {
                DiscrepancyType.Shortage => new[] { ExceptionType.Shortage },
                DiscrepancyType.Damage => new[] { ExceptionType.Damage },
                _ => new[] { ExceptionType.CustomerRefusal, ExceptionType.PartialDelivery },
            };
            open.Where(e => kinds.Contains(e.ExceptionType) && e.ClaimReference is null).ToList().ForEach(e => e.LinkClaim(reference.Reference));
            results.Add(new ClaimResultDto(d.Id, request.Sku, d.Type, d.Quantity, reference.Reference, reference.System));
        }

        return results;
    }
}

/// <summary>Raises claims for a delivery's shortages, damage and goods not accepted. For people who handle exceptions or review proofs.</summary>
internal sealed class ClaimHandler(DeliveriesDbContext db, DeliveryAccess access, ClaimService service)
{
    public async Task<Result<IReadOnlyList<ClaimResultDto>>> CreateAsync(Guid deliveryId, CreateClaimsRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageExceptions && !access.CanReview && !access.CanManage)
        {
            return DeliveryAccess.Forbidden;
        }

        var delivery = await db.Deliveries.Include(d => d.Items).Include(d => d.Discrepancies).AsSplitQuery().FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        var made = await service.CreateAsync(delivery, request.DiscrepancyIds, cancellationToken);
        if (made.Count == 0)
        {
            return Error.Conflict("claims.nothing_to_claim", "There is no shortage, damage or rejected quantity on this delivery that does not already have a claim.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(made);
    }

    public async Task<Result<BillingStatusDto>> BillingAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
        if (delivery is null || !access.CanSee(delivery))
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        var last = await db.IntegrationMessages.AsNoTracking().Where(m => m.DeliveryId == deliveryId && m.Target == "FreightAudit").OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (last is null)
        {
            return new BillingStatusDto("Unknown", false, false, null, "Nothing has been reported to freight audit for this delivery yet.");
        }

        var change = JsonSerializer.Deserialize<PodStatusChange>(last.PayloadJson, DeliverySettings.Json)!;
        return new BillingStatusDto(
            change.Status, change.BillingEligible, change.InvoiceHold, last.CreatedAt,
            change.BillingEligible ? "Freight audit may audit and pay this load." : change.Status == "Rejected" ? "The proof was rejected: the invoice stays on hold until it is corrected and accepted." : "The invoice is on hold until the proof is accepted.");
    }
}
