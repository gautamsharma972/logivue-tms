using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Queries;

internal sealed class AlertHandler(TrackingDbContext db, TrackingAccess access, TrackingHealthMonitor monitor, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<PagedResult<AlertDto>>> ListAsync(ListAlertsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead && !(access.IsVendor && access.CanExecute))
        {
            return TrackingAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var rows = db.Alerts.AsNoTracking().AsQueryable();
        if (access.IsVendor)
        {
            rows = rows.Where(a => a.TransporterId == access.VendorTransporterId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(a => a.Status == status);
        }

        if (query.Severity is { } severity)
        {
            rows = rows.Where(a => a.Severity == severity);
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(a => a.Type == type);
        }

        if (query.ShipmentId is { } shipmentId)
        {
            rows = rows.Where(a => a.TrackedShipmentId == shipmentId);
        }

        var now = clock.GetUtcNow();
        var page = await rows.OrderBy(a => a.Status == AlertStatus.Resolved ? 1 : 0).ThenByDescending(a => a.RaisedAt).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<AlertDto>(page.Items.Select(a => TrackingMapper.Alert(a, now)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public Task<Result<AlertDto>> AcknowledgeAsync(Guid id, CancellationToken cancellationToken) =>
        ActAsync(id, (a, now) => a.Acknowledge(user.UserId, now), cancellationToken);

    public Task<Result<AlertDto>> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken cancellationToken) =>
        ActAsync(id, (a, now) => a.Resolve(request.Note, now), cancellationToken);

    private async Task<Result<AlertDto>> ActAsync(Guid id, Func<TrackingAlert, DateTimeOffset, Result> act, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return TrackingAccess.Forbidden;
        }

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return TrackingAccess.AlertNotFound;
        }

        var now = clock.GetUtcNow();
        var result = act(alert, now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return TrackingMapper.Alert(alert, now);
    }
}

internal sealed class ExceptionHandler(TrackingDbContext db, TrackingAccess access, TrackingHealthMonitor monitor, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<PagedResult<TrackingExceptionSummaryDto>>> ListAsync(ListExceptionsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead && !(access.IsVendor && access.CanExecute))
        {
            return TrackingAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var rows = db.Exceptions.AsNoTracking().AsQueryable();
        if (access.IsVendor)
        {
            rows = rows.Where(e => e.TransporterId == access.VendorTransporterId);
        }
        else if (query.TransporterId is { } transporterId)
        {
            rows = rows.Where(e => e.TransporterId == transporterId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(e => e.Status == status);
        }

        if (query.Severity is { } severity)
        {
            rows = rows.Where(e => e.Severity == severity);
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(e => e.Type == type);
        }

        if (query.ShipmentId is { } shipmentId)
        {
            rows = rows.Where(e => e.TrackedShipmentId == shipmentId);
        }

        var now = clock.GetUtcNow();
        if (query.OpenOnly == true)
        {
            rows = rows.Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed);
        }

        if (query.Overdue == true)
        {
            rows = rows.Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed && e.DueAt < now);
        }

        var page = await rows.OrderByDescending(e => e.Severity == Severity.Critical ? 4 : e.Severity == Severity.High ? 3 : e.Severity == Severity.Warning ? 2 : 1).ThenBy(e => e.DueAt)
            .ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<TrackingExceptionSummaryDto>(page.Items.Select(e => TrackingMapper.Exception(e, now)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<TrackingExceptionDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, tracked: false, cancellationToken);
        return found.IsFailure ? found.Error : await ToDtoAsync(found.Value, cancellationToken);
    }

    public Task<Result<TrackingExceptionDto>> AcknowledgeAsync(Guid id, CancellationToken ct) => ActAsync(id, e => e.Acknowledge(user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<TrackingExceptionDto>> AssignAsync(Guid id, AssignExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Assign(request.OwnerUserId, request.Department, request.DueAt, request.Severity, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<TrackingExceptionDto>> EscalateAsync(Guid id, EscalateExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Escalate(request.Reason, request.Level, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<TrackingExceptionDto>> ResolveAsync(Guid id, ResolveExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Resolve(request.RootCause, request.DelayReason, request.ActionTaken, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<TrackingExceptionDto>> CloseAsync(Guid id, CancellationToken ct) => ActAsync(id, e => e.Close(user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<TrackingExceptionDto>> NoteAsync(Guid id, NoteRequest request, CancellationToken ct) => ActAsync(id, e => e.AddNote(request.Text, user.UserId, clock.GetUtcNow()), ct);

    private async Task<Result<TrackingExceptionDto>> ActAsync(Guid id, Func<TrackingException, Result> act, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return TrackingAccess.Forbidden;
        }

        var found = await FindAsync(id, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var result = act(found.Value);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(found.Value, cancellationToken);
    }

    private async Task<Result<TrackingException>> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        if (!access.CanRead && !(access.IsVendor && access.CanExecute) && !access.CanManage)
        {
            return TrackingAccess.Forbidden;
        }

        var query = db.Exceptions.Include(e => e.Notes).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var exception = await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return exception is null || !access.CanSeeTransporter(exception.TransporterId) ? TrackingAccess.ExceptionNotFound : exception;
    }

    private async Task<TrackingExceptionDto> ToDtoAsync(TrackingException e, CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == e.TrackedShipmentId, cancellationToken);
        return new TrackingExceptionDto(
            TrackingMapper.Exception(e, clock.GetUtcNow()), e.RootCause, e.DelayReason, e.ActionTaken, e.ResolvedAt, e.ClosedAt, e.EscalatedAt, shipment?.DriverName, shipment?.DriverPhone, shipment?.LastLatitude,
            shipment?.LastLongitude, shipment?.LastCapturedAt, e.Notes.OrderBy(n => n.At).Select(n => new ExceptionNoteDto(n.At, n.Text, n.By)).ToList(), e.Version);
    }
}
