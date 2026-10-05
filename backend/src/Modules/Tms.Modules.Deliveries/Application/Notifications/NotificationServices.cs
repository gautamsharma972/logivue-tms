using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application.Dashboard;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Notifications;

public sealed record NotificationDto(Guid Id, NotificationKind Kind, string Title, string Body, Guid? DeliveryId, Guid? PodId, Guid? ExceptionId, DateTimeOffset CreatedAt, bool Read);

public sealed record ListNotificationsQuery(bool? UnreadOnly = null, int Page = 1, int PageSize = 25);

/// <summary>Creates in-app notifications. A condition that stays true (a proof that is still overdue) notifies once: the dedupe key sees to that.</summary>
internal sealed class NotificationPublisher(DeliveriesDbContext db, ICurrentUser user, TimeProvider clock)
{
    public const string StaffExceptions = DeliveryPermissions.ExceptionsManage;
    public const string StaffReview = DeliveryPermissions.PodReview;

    public async Task<bool> PublishAsync(
        NotificationKind kind, string title, string body, Guid? deliveryId, Guid? podId, Guid? exceptionId, Guid? transporterId, string? permission, string dedupeKey, CancellationToken cancellationToken)
    {
        if (user.TenantId is not { } tenantId || (transporterId is null && permission is null))
        {
            return false;
        }

        if (db.Notifications.Local.Any(n => n.DedupeKey == dedupeKey) || await db.Notifications.AnyAsync(n => n.DedupeKey == dedupeKey, cancellationToken))
        {
            return false;
        }

        db.Notifications.Add(DeliveryNotification.Create(tenantId, kind, title, body, deliveryId, podId, exceptionId, transporterId, permission, dedupeKey, clock.GetUtcNow()));
        return true;
    }
}

/// <summary>
/// Notices the things nobody did: a proof not submitted in time, a review that is taking too long, a returned proof not corrected. There is no cross-tenant job,
/// so the check runs whenever the dashboard or the notifications are read, in the reader's own tenant.
/// </summary>
internal sealed class SlaMonitor(DeliveriesDbContext db, AgeingService ageing, NotificationPublisher publisher, DeliveryAccess access)
{
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var raised = false;
        foreach (var a in (await ageing.AgedAsync(null, cancellationToken)).Where(a => a.Overdue))
        {
            var r = a.Row;
            var hours = $"{a.Hours:0.0} h";
            switch (a.Stage)
            {
                case AgeingStage.PendingSubmission:
                    raised |= await publisher.PublishAsync(NotificationKind.PodOverdue, $"Proof overdue: {r.Number}", $"{r.Customer}: delivered but no proof submitted after {hours} (target {a.Target} h).", r.DeliveryId, r.PodId, null, r.TransporterId, null, $"pod-overdue:{r.DeliveryId}", cancellationToken);
                    raised |= await publisher.PublishAsync(NotificationKind.PodOverdue, $"Proof overdue: {r.Number}", $"{r.Transporter ?? "The transporter"} has not submitted the proof for {r.Customer} after {hours}.", r.DeliveryId, r.PodId, null, null, NotificationPublisher.StaffReview, $"pod-overdue-staff:{r.DeliveryId}", cancellationToken);
                    break;
                case AgeingStage.PendingReview:
                    raised |= await publisher.PublishAsync(NotificationKind.PodReviewOverdue, $"Review overdue: {r.Number}", $"The proof for {r.Customer} has waited {hours} for a decision (target {a.Target} h).", r.DeliveryId, r.PodId, null, null, NotificationPublisher.StaffReview, $"review-overdue:{r.PodId}:{(r.SubmittedAt ?? r.FirstSubmittedAt)?.UtcTicks}", cancellationToken);
                    break;
                default:
                    raised |= await publisher.PublishAsync(NotificationKind.ResubmissionOverdue, $"Corrected proof overdue: {r.Number}", $"The proof for {r.Customer} was sent back {hours} ago and has not been corrected (target {a.Target} h).", r.DeliveryId, r.PodId, null, r.TransporterId, null, $"resubmit-overdue:{r.PodId}:{r.ReturnedAt?.UtcTicks}", cancellationToken);
                    break;
            }
        }

        if (raised && !access.IsVendor)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (raised)
        {
            await db.SaveChangesAsync(cancellationToken); // a vendor reading its own items still records what it found: the keys keep it to once
        }
    }
}

/// <summary>A user's own notifications: their transporter's (for vendors) or the ones for the permissions they hold (staff). Read state is theirs alone.</summary>
internal sealed class NotificationHandler(DeliveriesDbContext db, DeliveryAccess access, SlaMonitor monitor, ICurrentUser user, TimeProvider clock)
{
    private IQueryable<DeliveryNotification> Mine()
    {
        var all = db.Notifications.AsNoTracking().AsQueryable();
        if (access.IsVendor)
        {
            var mine = access.VendorTransporterId;
            return all.Where(n => n.AudienceTransporterId == mine);
        }

        var permissions = user.Permissions.ToList();
        return all.Where(n => n.AudiencePermission != null && permissions.Contains(n.AudiencePermission));
    }

    private bool Allowed => access.IsVendor ? access.CanExecute : access.CanRead || access.CanManageExceptions || access.CanReview;

    public async Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        if (!Allowed || user.UserId is not { } me)
        {
            return 0;
        }

        return await Mine().CountAsync(n => !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me), cancellationToken);
    }

    public async Task<Result<PagedResult<NotificationDto>>> ListAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (!Allowed || user.UserId is not { } me)
        {
            return DeliveryAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var rows = Mine();
        if (query.UnreadOnly == true)
        {
            rows = rows.Where(n => !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me));
        }

        var page = await rows.OrderByDescending(n => n.CreatedAt).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var ids = page.Items.Select(n => n.Id).ToList();
        var read = (await db.NotificationReads.AsNoTracking().Where(r => r.UserId == me && ids.Contains(r.NotificationId)).Select(r => r.NotificationId).ToListAsync(cancellationToken)).ToHashSet();
        return new PagedResult<NotificationDto>(
            page.Items.Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.DeliveryId, n.PodId, n.ExceptionId, n.CreatedAt, read.Contains(n.Id))).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result> MarkReadAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (!Allowed || user.UserId is not { } me || user.TenantId is not { } tenantId)
        {
            return DeliveryAccess.Forbidden;
        }

        var unread = await Mine().Where(n => (id == null || n.Id == id) && !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me)).Select(n => n.Id).ToListAsync(cancellationToken);
        if (id is not null && unread.Count == 0 && !await Mine().AnyAsync(n => n.Id == id, cancellationToken))
        {
            return Error.NotFound("notifications.not_found", "Notification not found.");
        }

        foreach (var notificationId in unread)
        {
            db.NotificationReads.Add(NotificationRead.Create(tenantId, notificationId, me, clock.GetUtcNow()));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
