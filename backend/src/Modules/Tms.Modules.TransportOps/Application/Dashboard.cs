using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Application;

public sealed record DashboardDto(
    int OpenRequests, int ReadyForDispatch, int LrsNotOnTrip, int TripsActive, int LrsInTransit, int ArrivedAwaitingUnloading, int PodPending, int PodToReview, int PodRejected, int OldestPendingPodDays,
    int? BillingOnHold, int? ReadyToInvoice, int? InvoicesToSubmit, int? SubmissionsRejected, decimal? Outstanding, decimal? Overdue, decimal? DetentionThisMonth);
// The billing figures are null (not measurable for this person), never zero, for someone without ops.billing.read.

internal sealed class DashboardHandler(TransportOpsDbContext db, TimeProvider clock, ICurrentUser currentUser)
{
    public async Task<Result<DashboardDto>> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = clock.TodayInIndia();
        var monthStart = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.FromMinutes(330));
        var open = new[] { RequestStatus.Received, RequestStatus.UnderReview, RequestStatus.Incomplete, RequestStatus.Validated, RequestStatus.ReadyForDispatch };
        var pending = db.Pods.Where(p => p.Status == PodStatus.NotYetDue || p.Status == PodStatus.Pending || p.Status == PodStatus.Rejected)
            .Join(db.Lrs.Where(l => l.Status == DeliveryStatus.UnloadingCompleted), p => p.LrId, l => l.Id, (p, l) => l.UnloadedAt);
        var oldest = await pending.MinAsync(u => u, cancellationToken);
        var money = currentUser.Permissions.Contains(OpsPermissions.BillingRead);
        var issued = db.Invoices.Where(i => i.Status == InvoiceStatus.Issued && i.AmountPaid < i.GrandTotal);
        var outstanding = money ? await issued.SumAsync(i => (decimal?)(i.GrandTotal - i.AmountPaid), cancellationToken) ?? 0 : (decimal?)null;
        var overdue = money ? await issued.Where(i => i.DueDate < today).SumAsync(i => (decimal?)(i.GrandTotal - i.AmountPaid), cancellationToken) ?? 0 : (decimal?)null;
        var detention = money ? await db.Calculations.Where(c => c.Status != CalculationStatus.Superseded && c.CalculatedAt >= monthStart).SumAsync(c => (decimal?)(c.DetentionOverride ?? c.DetentionAmount), cancellationToken) ?? 0 : (decimal?)null;
        return new DashboardDto(
            await db.Requests.CountAsync(r => open.Contains(r.Status), cancellationToken),
            await db.Requests.CountAsync(r => r.Status == RequestStatus.ReadyForDispatch, cancellationToken),
            await db.Lrs.CountAsync(l => l.Status == DeliveryStatus.Created, cancellationToken),
            await db.Trips.CountAsync(t => t.Status != TripStatus.Completed && t.Status != TripStatus.Cancelled, cancellationToken),
            await db.Lrs.CountAsync(l => l.Status == DeliveryStatus.InTransit, cancellationToken),
            await db.Lrs.CountAsync(l => l.Status == DeliveryStatus.Arrived, cancellationToken),
            await pending.CountAsync(cancellationToken),
            await db.Pods.CountAsync(p => p.Status == PodStatus.Received || p.Status == PodStatus.UnderReview, cancellationToken),
            await db.Pods.CountAsync(p => p.Status == PodStatus.Rejected, cancellationToken),
            oldest is { } o ? Math.Max(0, (int)(now - o).TotalDays) : 0,
            money ? await db.Calculations.CountAsync(c => c.Status == CalculationStatus.OnHold, cancellationToken) : null,
            money ? await db.Calculations.CountAsync(c => c.Status == CalculationStatus.Reviewed && c.InvoiceId == null, cancellationToken) : null,
            money ? await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Issued && (i.Submission == SubmissionStatus.NotSubmitted || i.Submission == SubmissionStatus.ResubmissionRequired), cancellationToken) : null,
            money ? await db.Invoices.CountAsync(i => i.Submission == SubmissionStatus.ResubmissionRequired, cancellationToken) : null,
            outstanding, overdue, detention);
    }
}
