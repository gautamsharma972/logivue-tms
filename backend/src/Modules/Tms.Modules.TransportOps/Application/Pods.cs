using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record PodUploadResult(int Stored, int Duplicates, bool Replay);

/// <summary>
/// The one way a POD file is added, for the driver's QR page and for the office alike: the files are checked and stored, linked to the LR's POD as a numbered submission, and the POD
/// becomes Received. Earlier files are never replaced or deleted. Nothing here reads a location, and a POD is never accepted by being uploaded.
/// </summary>
internal static class PodUploads
{
    public static async Task<Result<PodUploadResult>> StoreAsync(
        TransportOpsDbContext db, FileIntake files, TimeProvider clock, Lr lr, Pod pod, IReadOnlyList<UploadedFile> uploads, PodSource source, Guid? userId, string? remarks, string? clientKey, PodSetting rules,
        CancellationToken cancellationToken)
    {
        if (clientKey is not null && pod.Documents.Any(d => d.ClientKey == $"{clientKey}:0"))
        {
            return new PodUploadResult(0, 0, true);
        }

        if (pod.Status == PodStatus.Accepted)
        {
            return Error.Conflict("ops.pod_accepted", "This POD has already been accepted and cannot be changed.");
        }

        var now = clock.GetUtcNow();
        var known = pod.Documents.Select(d => d.Sha256).ToHashSet();
        var stored = new List<StoredBlob>();
        var duplicates = 0;
        var sequence = pod.Documents.Count;
        foreach (var upload in uploads)
        {
            var blob = await files.SaveEvidenceAsync(upload.Content, upload.FileName, lr.TenantId, lr.Number, "POD", ++sequence, rules.MaxBytes, cancellationToken);
            if (blob.IsFailure)
            {
                foreach (var done in stored)
                {
                    await files.DeleteAsync(done.Key, cancellationToken);
                }

                return blob.Error;
            }

            if (!known.Add(blob.Value.Sha256))
            {
                duplicates++;
                await files.DeleteAsync(blob.Value.Key, cancellationToken);
                continue;
            }

            stored.Add(blob.Value);
        }

        if (stored.Count == 0)
        {
            return Error.Conflict("ops.pod_duplicate", "These files have already been uploaded for this LR.");
        }

        var submission = pod.BeginSubmission();
        var documents = stored.Select((b, i) => PodDocument.Create(pod.TenantId, pod.Id, submission, b.Key, b.SafeName, b.ContentType, b.SizeBytes, b.Sha256, source, userId, now, remarks,
            source == PodSource.QrWeb ? lr.TokenRef : null, clientKey is null ? null : $"{clientKey}:{i}")).ToList();
        var added = pod.AddDocuments(documents, now);
        if (added.IsFailure)
        {
            foreach (var done in stored)
            {
                await files.DeleteAsync(done.Key, cancellationToken);
            }

            return added.Error;
        }

        foreach (var document in documents)
        {
            db.PodDocuments.Add(document);
        }

        db.Events.Add(DeliveryEvent.Create(lr.TenantId, lr.TripId, lr.Id, EventType.PodUploaded, now, now, source == PodSource.QrWeb ? EventSource.QrWeb : EventSource.Office, userId,
            $"{stored.Count} file(s), submission {submission}", null, source == PodSource.QrWeb ? lr.TokenRef : null));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var done in stored)
            {
                await files.DeleteAsync(done.Key, cancellationToken);
            }

            throw;
        }

        return new PodUploadResult(stored.Count, duplicates, false);
    }
}

public sealed record PodDocumentDto(Guid Id, int Submission, string FileName, string ContentType, long SizeBytes, PodSource Source, DateTimeOffset UploadedAt, Guid? UploadedBy, string? Remarks);

public sealed record PodReviewDto(string Action, Guid? ReviewerId, DateTimeOffset At, string? Reason);

public sealed record PodDto(
    Guid Id, Guid LrId, string LrNumber, PodStatus Status, int Submission, DateTimeOffset? PendingAt, DateTimeOffset? ReceivedAt, Guid? ReviewerId, DateTimeOffset? ReviewedAt, string? RejectionReason,
    IReadOnlyList<PodDocumentDto> Documents, IReadOnlyList<PodReviewDto> Reviews, DeliveryStatus DeliveryStatus, DateTimeOffset? ArrivedAt, DateTimeOffset? UnloadedAt, long Version);

public sealed record PodQueueItemDto(
    Guid PodId, Guid LrId, string LrNumber, string CustomerName, string DestinationName, string? VehicleRegistration, PodStatus Status, DateTimeOffset? ArrivedAt, DateTimeOffset? UnloadedAt,
    DateTimeOffset? ReceivedAt, int AgeDays, int Submission, int Files);

public sealed record PodDecisionRequest(string? Reason);

internal sealed class PodDecisionRequestValidator : AbstractValidator<PodDecisionRequest>
{
    public PodDecisionRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

internal sealed class PodHandler(TransportOpsDbContext db, OpsAccess access, IOpsSettings settings, FileIntake files, TimeProvider clock)
{
    /// <summary>The office queue. "pending": delivered but no usable POD yet; "review": received and waiting for a decision; "all".</summary>
    public async Task<Result<PagedResult<PodQueueItemDto>>> QueueAsync(string? view, string? search, Guid? customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Pods.AsNoTracking().Join(db.Lrs.AsNoTracking(), p => p.LrId, l => l.Id, (p, l) => new { p, l });
        query = (view ?? "pending") switch
        {
            "review" => query.Where(x => x.p.Status == PodStatus.Received || x.p.Status == PodStatus.UnderReview),
            "all" => query,
            _ => query.Where(x => x.l.Status == DeliveryStatus.UnloadingCompleted && (x.p.Status == PodStatus.NotYetDue || x.p.Status == PodStatus.Pending || x.p.Status == PodStatus.Rejected)),
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x => x.l.Number.Contains(s) || (x.l.CustomerReference != null && x.l.CustomerReference.Contains(s)));
        }

        if (customerId is { } c)
        {
            query = query.Where(x => x.l.CustomerId == c);
        }

        var paged = await query.OrderBy(x => x.l.UnloadedAt).ToPagedAsync(page, pageSize, cancellationToken);
        var lrs = paged.Items.Select(x => x.l).ToList();
        var lookups = await new Lookups(db).LoadAsync(lrs, cancellationToken);
        var podIds = paged.Items.Select(x => x.p.Id).ToList();
        var fileCounts = await db.PodDocuments.AsNoTracking().Where(d => podIds.Contains(d.PodId)).GroupBy(d => d.PodId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var now = clock.GetUtcNow();
        var items = paged.Items.Select(x => new PodQueueItemDto(x.p.Id, x.l.Id, x.l.Number, lookups.Customers.GetValueOrDefault(x.l.CustomerId, "?"),
            lookups.Locations.TryGetValue(x.l.DestinationLocationId, out var d) ? d.Name : "?", x.l.VehicleRegistration, x.p.Status, x.l.ArrivedAt, x.l.UnloadedAt, x.p.ReceivedAt,
            x.l.UnloadedAt is { } u ? Math.Max(0, (int)(now - u).TotalDays) : 0, x.p.Submission, fileCounts.GetValueOrDefault(x.p.Id))).ToList();
        return new PagedResult<PodQueueItemDto>(items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<PodDto>> GetByLrAsync(Guid lrId, CancellationToken cancellationToken)
    {
        var pod = await db.Pods.AsNoTracking().Include(p => p.Documents).Include(p => p.Reviews).FirstOrDefaultAsync(p => p.LrId == lrId, cancellationToken);
        return pod is null ? OpsErrors.PodNotFound : await ToDtoAsync(pod, cancellationToken);
    }

    /// <summary>Office upload against the same LR, later than the delivery. No location is involved.</summary>
    public async Task<Result<PodDto>> UploadAsync(Guid lrId, IReadOnlyList<UploadedFile> uploads, string? remarks, string? clientKey, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.FirstOrDefaultAsync(l => l.Id == lrId, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (lr.Status != DeliveryStatus.UnloadingCompleted)
        {
            return Error.Conflict("ops.pod_state", "A POD can be uploaded once unloading has been confirmed for the LR. If the driver did not confirm it, record the arrival and unloading times first.");
        }

        var rules = await settings.GetAsync(OpsSettingKeys.Pod, new PodSetting(), cancellationToken);
        if (uploads.Count == 0 || uploads.Count > rules.EffectiveMaxFiles)
        {
            return Error.Validation("ops.pod_files", $"Choose between 1 and {rules.EffectiveMaxFiles} files.");
        }

        var pod = await db.Pods.Include(p => p.Documents).Include(p => p.Reviews).FirstAsync(p => p.LrId == lrId, cancellationToken);
        var stored = await PodUploads.StoreAsync(db, files, clock, lr, pod, uploads, PodSource.Office, access.UserId, remarks, string.IsNullOrWhiteSpace(clientKey) ? null : clientKey.Trim(), rules, cancellationToken);
        return stored.IsFailure ? stored.Error : await ToDtoAsync(pod, cancellationToken);
    }

    public Task<Result<PodDto>> StartReviewAsync(Guid lrId, CancellationToken cancellationToken) =>
        DecideAsync(lrId, (pod, now) => pod.StartReview(access.UserId, now), cancellationToken);

    public Task<Result<PodDto>> AcceptAsync(Guid lrId, PodDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(lrId, (pod, now) => pod.Accept(access.UserId, now, request.Reason), cancellationToken);

    public Task<Result<PodDto>> RejectAsync(Guid lrId, PodDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(lrId, (pod, now) => pod.Reject(access.UserId, now, request.Reason ?? string.Empty), cancellationToken);

    /// <summary>Reopens an accepted POD for a second look. Refused once the LR has been put on an invoice.</summary>
    public async Task<Result<PodDto>> ReopenAsync(Guid lrId, PodDecisionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation("ops.pod_reason", "A reason is required to reopen a POD.");
        }

        if (await db.Calculations.AnyAsync(c => c.LrId == lrId && c.InvoiceId != null && c.Status == CalculationStatus.Invoiced, cancellationToken))
        {
            return Error.Conflict("ops.pod_invoiced", "This LR has already been invoiced, so its POD cannot be reopened.");
        }

        return await DecideAsync(lrId, (pod, now) => pod.Reopen(access.UserId, now, request.Reason), cancellationToken);
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> OpenDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.PodDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        var stream = document is null ? null : await files.OpenAsync(document.FileKey, cancellationToken);
        return document is null || stream is null ? OpsErrors.FileNotFound : (stream, document.ContentType, document.FileName);
    }

    private async Task<Result<PodDto>> DecideAsync(Guid lrId, Func<Pod, DateTimeOffset, Result> action, CancellationToken cancellationToken)
    {
        var pod = await db.Pods.Include(p => p.Documents).Include(p => p.Reviews).FirstOrDefaultAsync(p => p.LrId == lrId, cancellationToken);
        if (pod is null)
        {
            return OpsErrors.PodNotFound;
        }

        var result = action(pod, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        foreach (var review in pod.Reviews)
        {
            if (db.Entry(review).State == EntityState.Detached)
            {
                db.Entry(review).State = EntityState.Added;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(pod, cancellationToken);
    }

    private async Task<PodDto> ToDtoAsync(Pod pod, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.AsNoTracking().FirstAsync(l => l.Id == pod.LrId, cancellationToken);
        return new PodDto(pod.Id, pod.LrId, lr.Number, pod.Status, pod.Submission, pod.PendingAt, pod.ReceivedAt, pod.ReviewerId, pod.ReviewedAt, pod.RejectionReason,
            pod.Documents.OrderBy(d => d.UploadedAt).Select(d => new PodDocumentDto(d.Id, d.Submission, d.FileName, d.ContentType, d.SizeBytes, d.Source, d.UploadedAt, d.UploadedBy, d.Remarks)).ToList(),
            pod.Reviews.OrderBy(r => r.At).Select(r => new PodReviewDto(r.Action, r.ReviewerId, r.At, r.Reason)).ToList(), lr.Status, lr.ArrivedAt, lr.UnloadedAt, pod.Version);
    }
}
