using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Queries;

/// <summary>
/// POD summary read from this module's own POD records. Replace with the POD module's adapter when it exists;
/// the KPI pipeline depends only on <see cref="ITransporterPodProvider"/>.
/// </summary>
internal sealed class LocalPodProvider(TransporterDbContext db, TimeProvider clock) : ITransporterPodProvider
{
    public async Task<TransporterPodSummaryDto> GetPodSummaryAsync(long transporterId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.PodRecords.AsNoTracking()
            .Where(p => p.TransporterId == transporterId && p.DeliveredAt >= from && p.DeliveredAt < to)
            .Select(p => new { p.Status, p.DueAt, p.DeliveredAt, p.SubmittedAt, p.SubmittedWithinSla, p.RejectionCount })
            .ToListAsync(cancellationToken);

        // A delivery is measured once its POD is in or its SLA has passed. Deliveries still inside the SLA are not counted.
        var required = rows.Where(r => r.SubmittedAt is not null || r.DueAt <= now).ToList();
        var hours = rows.Where(r => r.SubmittedAt is not null).Select(r => (r.SubmittedAt!.Value - r.DeliveredAt).TotalHours).ToList();

        return new TransporterPodSummaryDto(
            TransporterId: transporterId,
            DeliveriesRequiringPod: required.Count,
            SubmittedWithinSla: required.Count(r => r.SubmittedWithinSla == true),
            Accepted: rows.Count(r => r.Status == PodStatus.Accepted),
            Rejected: rows.Count(r => r.RejectionCount > 0),
            Reviewed: rows.Count(r => r.Status == PodStatus.Accepted || r.RejectionCount > 0),
            Pending: rows.Count(r => r.Status is PodStatus.Pending or PodStatus.ResubmissionRequired),
            AverageSubmissionHours: hours.Count == 0 ? null : Math.Round(hours.Average(), 2));
    }
}
