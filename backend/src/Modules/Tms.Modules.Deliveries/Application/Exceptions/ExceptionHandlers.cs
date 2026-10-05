using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Exceptions;

/// <summary>Delivery exceptions: listing, raising by hand, and the moves an owner makes (acknowledge, assign, investigate, escalate, resolve, close).</summary>
internal sealed class ExceptionHandler(DeliveriesDbContext db, DeliveryAccess access, ExceptionFactory factory, Notifications.NotificationPublisher notifications, IFileStore files, IFileScanner scanner, ICurrentUser user, TimeProvider clock)
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
        ActAsync(id, async e =>
        {
            var escalated = e.Escalate(request.Reason, user.UserId, clock.GetUtcNow());
            if (escalated.IsSuccess)
            {
                await notifications.PublishAsync(Domain.NotificationKind.ExceptionEscalated, $"Escalated: {e.Number}", $"{e.DeliveryNumber}: {request.Reason.Trim()}", e.DeliveryId, e.PodId, e.Id, null, Notifications.NotificationPublisher.StaffExceptions, $"escalated:{e.Id}", ct);
            }

            return escalated;
        }, ct);

    public Task<Result<ExceptionDto>> NoteAsync(Guid id, NoteRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.AddNote(request.Text, user.UserId, clock.GetUtcNow()), ct);

    public Task<Result<ExceptionDto>> ResolveAsync(Guid id, ResolveExceptionRequest request, CancellationToken ct) =>
        ActAsync(id, e => e.Resolve(request.Resolution, request.RootCause, request.ResponsibleParty, request.ActionTaken, request.FinancialImpact, request.ClaimReference, user.UserId, clock.GetUtcNow()), ct);

    /// <summary>Keeps a photo or document with the exception. Checked by its first bytes, never by the name or type it claims; stored privately.</summary>
    public async Task<Result<ExceptionDto>> AttachAsync(Guid id, AttachToExceptionForm form, CancellationToken cancellationToken)
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

        if (form.File is not { Length: > 0 } file)
        {
            return Error.Validation("exceptions.file_required", "Attach a file.");
        }

        if (file.Length > FileSniffer.MaxBytes)
        {
            return Error.Validation("exceptions.file_too_large", $"Files can be at most {FileSniffer.MaxBytes / (1024 * 1024)} MB.");
        }

        await using var upload = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await upload.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (FileSniffer.Identify(bytes.AsSpan(0, Math.Min(bytes.Length, 8))) is not var (contentType, extension))
        {
            return Error.Validation("exceptions.file_type", "Attach a PDF, JPG or PNG file.");
        }

        var scan = await scanner.ScanAsync(bytes, contentType, cancellationToken);
        if (!scan.IsClean)
        {
            return Error.Validation("exceptions.file_infected", scan.Detail ?? "The file did not pass the security scan.");
        }

        var now = clock.GetUtcNow();
        var key = $"exceptions/{now:yyyy}/{now:MM}/{found.Value.DeliveryId}/{found.Value.Id}/{Guid.CreateVersion7()}{extension}";
        var name = Path.GetFileName(file.FileName) is { Length: > 0 } n ? n[..Math.Min(n.Length, 255)] : $"attachment{extension}";
        var attached = found.Value.Attach(key, name, contentType, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), form.Note, user.UserId, now);
        if (attached.IsFailure)
        {
            return attached.Error;
        }

        memory.Position = 0;
        await files.SaveAsync(key, memory, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(found.Value, cancellationToken);
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> OpenAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await db.ExceptionAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        var owner = attachment is null ? null : await db.Exceptions.AsNoTracking().Where(e => e.Id == attachment.ExceptionId).Select(e => new { e.TransporterId }).FirstOrDefaultAsync(cancellationToken);
        if (attachment is null || owner is null || !access.CanSeeTransporter(owner.TransporterId))
        {
            return DeliveryAccess.ExceptionNotFound;
        }

        var stream = await files.OpenReadAsync(attachment.FileKey, cancellationToken);
        return stream is null ? Error.NotFound("exceptions.file_missing", "The file is no longer available.") : (stream, attachment.ContentType, attachment.FileName);
    }

    public Task<Result<ExceptionDto>> CloseAsync(Guid id, CancellationToken ct) => ActAsync(id, e => e.Close(user.UserId, clock.GetUtcNow()), ct);

    private Task<Result<ExceptionDto>> ActAsync(Guid id, Func<DeliveryException, Result> act, CancellationToken cancellationToken) =>
        ActAsync(id, e => Task.FromResult(act(e)), cancellationToken);

    private async Task<Result<ExceptionDto>> ActAsync(Guid id, Func<DeliveryException, Task<Result>> act, CancellationToken cancellationToken)
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

        var result = await act(found.Value);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(found.Value, cancellationToken);
    }

    private async Task<Result<DeliveryException>> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var query = db.Exceptions.Include(e => e.Notes).Include(e => e.Attachments).AsSplitQuery();
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
            e.Notes.OrderBy(n => n.At).Select(n => new ExceptionNoteDto(n.At, n.Text, n.By)).ToList(), e.Version,
            e.Attachments.OrderBy(a => a.At).Select(a => new ExceptionAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Note, a.At, a.By)).ToList());
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
