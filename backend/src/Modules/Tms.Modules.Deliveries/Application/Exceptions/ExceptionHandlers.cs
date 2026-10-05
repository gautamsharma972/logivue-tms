using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Exceptions;

/// <summary>Delivery exceptions: listing, raising by hand, and the moves an owner makes (acknowledge, assign, investigate, escalate, resolve, close).</summary>
internal sealed class ExceptionHandler(DeliveriesDbContext db, DeliveryAccess access, ExceptionFactory factory, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<PagedResult<ExceptionSummaryDto>>> ListAsync(ListExceptionsQuery query, CancellationToken cancellationToken)
    {
        var rows = db.Exceptions.AsNoTracking().AsQueryable();
        if (access.IsVendor)
        {
            if (access.VendorTransporterId is not { } mine || !access.CanSeeTransporter(mine))
            {
                return DeliveryAccess.Forbidden;
            }

            rows = rows.Where(e => e.TransporterId == mine);
        }
        else if (!access.CanRead)
        {
            return DeliveryAccess.Forbidden;
        }
        else if (query.TransporterId is { } transporterId)
        {
            rows = rows.Where(e => e.TransporterId == transporterId);
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(e => e.ExceptionType == type);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(e => e.Status == status);
        }

        if (query.Severity is { } severity)
        {
            rows = rows.Where(e => e.Severity == severity);
        }

        if (query.DeliveryId is { } deliveryId)
        {
            rows = rows.Where(e => e.DeliveryId == deliveryId);
        }

        if (query.OpenOnly == true)
        {
            rows = rows.Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed);
        }

        var now = clock.GetUtcNow();
        if (query.Overdue == true)
        {
            rows = rows.Where(e => e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed && e.DueAt < now);
        }

        var page = await rows.OrderByDescending(e => e.Severity).ThenBy(e => e.DueAt).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<ExceptionSummaryDto>(await SummariesAsync(page.Items.ToList(), now, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<ExceptionDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await FindAsync(id, tracked: false, cancellationToken);
        return found.IsFailure ? found.Error : await ToDtoAsync(found.Value, cancellationToken);
    }

    /// <summary>A person records something the system did not notice by itself (a customer's phone call, a reviewer's concern).</summary>
    public async Task<Result<ExceptionDto>> RaiseAsync(RaiseExceptionRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageExceptions && !access.CanReview && !access.CanManage)
        {
            return DeliveryAccess.Forbidden;
        }

        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.DeliveryId, cancellationToken);
        if (delivery is null)
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        var pod = await db.Pods.AsNoTracking().Where(p => p.DeliveryId == delivery.Id && p.IsCurrent).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        var raised = await factory.RaiseAsync(delivery, pod, request.Type, request.Description, cancellationToken, request.Severity);
        if (raised is null)
        {
            return Error.Conflict("exceptions.already_open", $"A {request.Type} exception is already open for {delivery.Number}.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(raised, cancellationToken);
    }

    public Task<Result<ExceptionDto>> AcknowledgeAsync(Guid id, CancellationToken ct) => ActAsync(id, e => e.Acknowledge(user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> AssignAsync(Guid id, AssignExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Assign(request.OwnerUserId, request.Department, request.DueAt, request.Severity, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> InvestigateAsync(Guid id, InvestigateExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Investigate(request.RootCause, request.ResponsibleParty, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> EscalateAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Escalate(request.Reason, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> NoteAsync(Guid id, NoteRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.AddNote(request.Text, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> ResolveAsync(Guid id, ResolveExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Resolve(request.Resolution, request.RootCause, request.ResponsibleParty, request.ActionTaken, request.FinancialImpact, request.ClaimReference, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> CloseAsync(Guid id, CancellationToken ct) => ActAsync(id, e => e.Close(user.UserId, clock.GetUtcNow()), ct);

    private async Task<Result<ExceptionDto>> ActAsync(Guid id, Func<DeliveryException, Result> act, CancellationToken cancellationToken)
    {
        if (!access.CanManageExceptions)
        {
            return DeliveryAccess.Forbidden;
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

    private async Task<Result<DeliveryException>> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var query = db.Exceptions.Include(e => e.Notes).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var exception = await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return exception is null || !access.CanSeeTransporter(exception.TransporterId) ? DeliveryAccess.ExceptionNotFound : exception;
    }

    private async Task<ExceptionDto> ToDtoAsync(DeliveryException e, CancellationToken cancellationToken)
    {
        var summary = (await SummariesAsync([e], clock.GetUtcNow(), cancellationToken))[0];
        return new ExceptionDto(
            summary, e.Description, e.RootCause, e.ResponsibleParty, e.ActionTaken, e.Resolution, e.FinancialImpact, e.ResolvedAt, e.EscalatedAt,
            e.Notes.OrderBy(n => n.At).Select(n => new ExceptionNoteDto(n.At, n.Text, n.By)).ToList(), e.Version);
    }

    private async Task<IReadOnlyList<ExceptionSummaryDto>> SummariesAsync(IReadOnlyList<DeliveryException> rows, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.DeliveryId).Distinct().ToList();
        var deliveries = await db.Deliveries.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.CustomerName, d.TransporterReference, d.VehicleReference }).ToDictionaryAsync(d => d.Id, cancellationToken);
        return rows.Select(e =>
        {
            deliveries.TryGetValue(e.DeliveryId, out var d);
            return new ExceptionSummaryDto(
                e.Id, e.Number, e.DeliveryId, e.DeliveryNumber, d?.CustomerName, d?.TransporterReference, d?.VehicleReference, e.PodId, e.ExceptionType, e.Severity, e.Status,
                e.OwnerUserId, e.Department, e.RaisedAt, e.DueAt, e.IsOpen && e.DueAt < now, Math.Round((e.ResolvedAt ?? now).Subtract(e.RaisedAt).TotalHours, 1), e.ClaimReference);
        }).ToList();
    }
}
