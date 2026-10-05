using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Integration;

/// <summary>
/// The claims system until a real one is connected: it keeps the complete hand-off (so a claims module, or a person, can pick it up) and gives back a reference.
/// Registered only if nothing else implements <see cref="IClaimsIntegration"/>.
/// </summary>
internal sealed class LocalClaimsIntegration(DeliveriesDbContext db, ISequenceGenerator sequences, ICurrentUser user, TimeProvider clock) : IClaimsIntegration
{
    public const string System = "local";

    public async Task<ClaimReference> CreateClaimAsync(ClaimRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = user.TenantId ?? throw new InvalidOperationException("A claim needs a tenant.");
        var reference = $"CLM-L-{await sequences.NextAsync(tenantId, "claim", cancellationToken):D5}";
        db.ClaimHandoffs.Add(ClaimHandoff.Create(tenantId, request.DeliveryId, null, reference, System, JsonSerializer.Serialize(request, DeliverySettings.Json), clock.GetUtcNow()));
        return new ClaimReference(reference, System);
    }
}

/// <summary>
/// Freight audit until a real one is connected: every proof status change is kept as a message with whether the load may now be billed or its invoice held, so
/// eligibility can be read back and nothing is lost. A repeat of the same change is ignored.
/// </summary>
internal sealed class LocalFreightAuditIntegration(DeliveriesDbContext db, ICurrentUser user, TimeProvider clock) : IFreightAuditIntegration
{
    public async Task NotifyPodStatusChangedAsync(PodStatusChange change, CancellationToken cancellationToken = default)
    {
        var tenantId = user.TenantId ?? throw new InvalidOperationException("A status change needs a tenant.");
        var kind = $"Pod{change.Status}";
        if (db.IntegrationMessages.Local.Any(m => m.DeliveryId == change.DeliveryId && m.Kind == kind && m.PodId == change.PodId)
            || await db.IntegrationMessages.AnyAsync(m => m.DeliveryId == change.DeliveryId && m.Kind == kind && m.PodId == change.PodId && m.Target == "FreightAudit", cancellationToken))
        {
            return;
        }

        db.IntegrationMessages.Add(IntegrationMessage.Create(tenantId, "FreightAudit", kind, change.DeliveryId, change.PodId, JsonSerializer.Serialize(change, DeliverySettings.Json), clock.GetUtcNow()));
    }
}

/// <summary>How dependably deliveries on a lane have gone, read from this module's own history for planning to weigh.</summary>
internal sealed class DeliveryReliabilityFeed(DeliveriesDbContext db, TimeProvider clock) : IDeliveryReliabilityFeed
{
    public async Task<LaneReliability> GetLaneReliabilityAsync(string originCity, string destinationCity, int days, CancellationToken cancellationToken = default)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 730));
        var rows = await (from d in db.Deliveries.AsNoTracking()
                          where d.OriginReference == originCity && d.DestinationReference == destinationCity && (d.ActualDeliveryAt ?? d.PlannedDeliveryAt) >= since
                              && (d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.PartiallyDelivered || d.Status == DeliveryStatus.Closed || d.Status == DeliveryStatus.Failed || d.Status == DeliveryStatus.Refused)
                          join p in db.Pods.AsNoTracking().Where(x => x.IsCurrent) on d.Id equals p.DeliveryId into ps
                          from p in ps.DefaultIfEmpty()
                          select new { d.Status, d.Outcome, d.ActualDeliveryAt, d.WindowEnd, Submitted = p == null ? null : p.FirstSubmittedAt }).ToListAsync(cancellationToken);

        var timed = rows.Where(r => r.WindowEnd != null && r.ActualDeliveryAt != null && r.Outcome is not (DeliveryOutcome.Failed or DeliveryOutcome.Refused)).ToList();
        var proofs = rows.Where(r => r.Submitted != null && r.ActualDeliveryAt != null).Select(r => (r.Submitted!.Value - r.ActualDeliveryAt!.Value).TotalHours).ToList();
        static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round((decimal)part / whole, 4);
        return new LaneReliability(
            originCity, destinationCity, days, rows.Count, Rate(timed.Count(r => r.ActualDeliveryAt <= r.WindowEnd), timed.Count), Rate(rows.Count(r => r.Status == DeliveryStatus.Failed), rows.Count),
            Rate(rows.Count(r => r.Status == DeliveryStatus.Refused), rows.Count), proofs.Count == 0 ? null : Math.Round((decimal)proofs.Average(), 1));
    }
}
