using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application.Deliveries;
using Tms.Modules.Deliveries.Application.Exceptions;
using Tms.Modules.Deliveries.Application.Ocr;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Pods;

internal static class PodQueries
{
    public static IQueryable<PodRecord> WithAll(this IQueryable<PodRecord> q) =>
        q.Include(p => p.Items).Include(p => p.Evidence).Include(p => p.Signatures).Include(p => p.Validations).Include(p => p.Reviews).Include(p => p.OcrResults).ThenInclude(o => o.Fields);
}

internal sealed class ListPodsHandler(DeliveriesDbContext db, DeliveryAccess access, IDeliverySettings settings, TimeProvider clock)
{
    public async Task<Result<PagedResult<PodSummaryDto>>> HandleAsync(ListPodsQuery query, bool reviewQueue, CancellationToken cancellationToken)
    {
        var rows = db.Pods.AsNoTracking().Include(p => p.Validations).Include(p => p.OcrResults).AsQueryable();
        if (access.IsVendor)
        {
            if (access.VendorTransporterId is not { } mine || !access.CanSeeTransporter(mine))
            {
                return DeliveryAccess.Forbidden;
            }

            rows = rows.Where(p => db.Deliveries.Any(d => d.Id == p.DeliveryId && d.TransporterId == mine));
        }
        else if (!access.CanRead || (reviewQueue && !access.CanReview))
        {
            return DeliveryAccess.Forbidden;
        }
        else if (query.TransporterId is { } transporterId)
        {
            rows = rows.Where(p => db.Deliveries.Any(d => d.Id == p.DeliveryId && d.TransporterId == transporterId));
        }

        if (query.CurrentOnly)
        {
            rows = rows.Where(p => p.IsCurrent);
        }

        if (reviewQueue)
        {
            rows = rows.Where(p => p.Status == PodStatus.Submitted || p.Status == PodStatus.UnderReview);
        }
        else if (query.Status is { } status)
        {
            rows = rows.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(p => p.PodNumber.Contains(term) || db.Deliveries.Any(d => d.Id == p.DeliveryId && (d.Number.Contains(term) || d.CustomerName.Contains(term) || (d.ShipmentReference != null && d.ShipmentReference.Contains(term)))));
        }

        if (query.Overdue == true)
        {
            var sla = await settings.GetAsync<SlaSetting>(DeliverySettingKeys.Sla, cancellationToken);
            var cutoff = clock.GetUtcNow().AddHours(-sla.PodReviewHours);
            rows = rows.Where(p => (p.Status == PodStatus.Submitted || p.Status == PodStatus.UnderReview) && p.SubmittedAt < cutoff);
        }

        var page = await (reviewQueue ? rows.OrderBy(p => p.SubmittedAt) : rows.OrderByDescending(p => p.CapturedAt)).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var deliveryIds = page.Items.Select(p => p.DeliveryId).Distinct().ToList();
        var deliveries = await db.Deliveries.AsNoTracking().Include(d => d.Discrepancies).Where(d => deliveryIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var items = page.Items.Select(p => DeliveryMapper.Summary(p, deliveries[p.DeliveryId], p.OcrResults.OrderByDescending(o => o.QueuedAt).FirstOrDefault()?.ProcessingStatus)).ToList();
        return new PagedResult<PodSummaryDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

/// <summary>Everything about proofs of delivery: reading, adding evidence, submitting, reviewing, correcting, and the reading of the paper document.</summary>
internal sealed class PodHandler(
    DeliveriesDbContext db, DeliveryAccess access, IDeliverySettings settings, DeliveryMapper mapper, PodEngine engine, ExceptionFactory exceptions, IFileStore files,
    IPodOcrQueue ocrQueue, IPodOcrService ocr, Notifications.NotificationPublisher notifications, ICurrentUser user, TimeProvider clock)
{
    private const int MaxPhotoBytes = 10 * 1024 * 1024;

    // ---- reading

    public async Task<Result<PodDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: false, write: false, cancellationToken);
        return loaded.IsFailure ? loaded.Error : await mapper.ToDtoAsync(loaded.Value.Pod, loaded.Value.Delivery, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<EvidenceDto>>> EvidenceAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: false, write: false, cancellationToken);
        return loaded.IsFailure ? loaded.Error : loaded.Value.Pod.Evidence.OrderBy(e => e.CapturedAt).Select(DeliveryMapper.ToDto).ToList();
    }

    public async Task<Result<PodReviewDto>> ReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return DeliveryAccess.Forbidden;
        }

        var loaded = await LoadAsync(id, tracked: false, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var podDto = await mapper.ToDtoAsync(pod, delivery, cancellationToken);
        var document = pod.ActiveEvidence.Where(e => e.EvidenceType == EvidenceType.PodDocument).OrderByDescending(e => e.CapturedAt).FirstOrDefault();
        return new PodReviewDto(podDto, await mapper.ToDtoAsync(delivery, cancellationToken), document?.Id, podDto.Ocr.Count > 0 ? podDto.Ocr[^1] : null);
    }

    /// <summary>The file behind a piece of evidence or a signature, for an authorised caller only: files are never publicly addressable.</summary>
    public async Task<Result<(Stream Content, string ContentType, string FileName)>> OpenEvidenceAsync(Guid evidenceId, CancellationToken cancellationToken)
    {
        var evidence = await db.Evidence.AsNoTracking().FirstOrDefaultAsync(e => e.Id == evidenceId, cancellationToken);
        if (evidence is null || !await CanSeePodAsync(evidence.PodId, cancellationToken))
        {
            return DeliveryAccess.PodNotFound;
        }

        var stream = await files.OpenReadAsync(evidence.FileKey, cancellationToken);
        return stream is null ? Error.NotFound("pods.file_missing", "The file is no longer available.") : (stream, evidence.ContentType, evidence.FileName);
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> OpenSignatureAsync(Guid signatureId, CancellationToken cancellationToken)
    {
        var signature = await db.Signatures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == signatureId, cancellationToken);
        if (signature is null || !await CanSeePodAsync(signature.PodId, cancellationToken))
        {
            return DeliveryAccess.PodNotFound;
        }

        var stream = await files.OpenReadAsync(signature.FileKey, cancellationToken);
        return stream is null ? Error.NotFound("pods.file_missing", "The file is no longer available.") : (stream, "image/png", "signature.png");
    }

    // ---- building the proof

    public async Task<Result<PodDto>> CreateAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        if (!access.CanManage && !access.CanExecute)
        {
            return DeliveryAccess.Forbidden;
        }

        var delivery = await db.Deliveries.Include(d => d.Items).Include(d => d.Discrepancies).AsSplitQuery().FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
        if (delivery is null || !access.CanSee(delivery))
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        if (!delivery.IsCompleted)
        {
            return Error.Conflict("pods.delivery_not_completed", $"A proof can only be started for a completed delivery; this one is {delivery.Status}.");
        }

        var existing = await db.Pods.WithAll().AsSplitQuery().FirstOrDefaultAsync(p => p.DeliveryId == deliveryId && p.IsCurrent, cancellationToken);
        if (existing is not null)
        {
            return await mapper.ToDtoAsync(existing, delivery, cancellationToken);
        }

        var podRules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken);
        var pod = PodRecord.Create(delivery, $"POD-{delivery.Number["DLV-".Length..]}", new GeoFix(delivery.ArrivalLatitude, delivery.ArrivalLongitude, delivery.ArrivalAccuracy),
            Geo.Check(delivery.CustomerLatitude, delivery.CustomerLongitude, delivery.GeofenceRadiusM, delivery.ArrivalLatitude, delivery.ArrivalLongitude, delivery.ArrivalAccuracy, podRules.MaxGpsAccuracyM), null, clock.GetUtcNow());
        pod.CopyOtp(delivery);
        db.Pods.Add(pod);
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(pod, delivery, cancellationToken);
    }

    public Task<Result<PodDto>> UpdateProofAsync(Guid id, UpdateProofRequest request, CancellationToken cancellationToken) =>
        MutateAsync(id, async (pod, delivery) =>
        {
            var p = request.Proof;
            var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken);
            if (p.Method == ProofMethod.Contactless && !rules.ContactlessAllowed)
            {
                return Error.Validation("pods.contactless_not_allowed", "Contactless delivery is not allowed.");
            }

            var set = pod.SetProof(p.Method, p.RecipientName, p.RecipientDesignation, p.RecipientPhone, p.RecipientRemarks, p.DriverConfirmed);
            if (set.IsSuccess && p.CustomerAcknowledged)
            {
                pod.AcknowledgeByCustomer();
                delivery.AcknowledgeDiscrepancies();
            }

            pod.CopyOtp(delivery);
            return set;
        }, cancellationToken);

    public async Task<Result<EvidenceDto>> AddEvidenceAsync(Guid id, UploadEvidenceForm form, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: true, write: true, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var clientId = Clean(idempotencyKey) ?? Clean(form.ClientRecordId);
        if (clientId is not null && pod.Evidence.FirstOrDefault(e => e.ClientRecordId == clientId) is { } already)
        {
            return DeliveryMapper.ToDto(already); // a retry: the first upload stands
        }

        if (form.File is not { Length: > 0 } file)
        {
            return Error.Validation("pods.file_required", "Attach a file.");
        }

        var images = await settings.GetAsync<ImageRulesSetting>(DeliverySettingKeys.Images, cancellationToken);
        if (file.Length > Math.Min(images.MaxBytes, FileSniffer.MaxBytes))
        {
            return Error.Validation("pods.file_too_large", $"Files can be at most {Math.Min(images.MaxBytes, FileSniffer.MaxBytes) / (1024 * 1024)} MB.");
        }

        await using var upload = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await upload.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        var sniffed = FileSniffer.Identify(bytes.AsSpan(0, Math.Min(bytes.Length, 8)));
        var isDocument = form.Type == EvidenceType.PodDocument;
        if (sniffed is not var (contentType, extension) || (!isDocument && contentType == "application/pdf"))
        {
            return Error.Validation("pods.file_type", isDocument ? "Upload a PDF, JPG or PNG file." : "Photos must be JPG or PNG images.");
        }

        var warnings = new List<string>();
        int? width = null;
        int? height = null;
        if (contentType != "application/pdf")
        {
            if (ImageProbe.Size(bytes) is not { } size)
            {
                return Error.Validation("pods.image_unreadable", "The image could not be read; it may be corrupted. Take the photo again.");
            }

            (width, height) = size;
            if (size.Width < images.MinWidth || size.Height < images.MinHeight)
            {
                var message = $"The image is {size.Width}×{size.Height}, below the {images.MinWidth}×{images.MinHeight} needed to be legible.";
                if (images.RejectLowResolution)
                {
                    return Error.Validation("pods.image_low_resolution", message);
                }

                warnings.Add(message);
            }
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (pod.ActiveEvidence.Any(e => e.FileHash == hash))
        {
            return Error.Conflict("pods.duplicate_file", "That exact file is already on this proof.");
        }

        if (await db.Evidence.AnyAsync(e => e.FileHash == hash && e.PodId != pod.Id && e.RemovedAt == null, cancellationToken))
        {
            warnings.Add("This file also appears on another proof.");
        }

        var key = $"pods/{clock.GetUtcNow():yyyy}/{clock.GetUtcNow():MM}/{delivery.Id}/{pod.Id}/{Guid.CreateVersion7()}{extension}";
        var at = form.CapturedAt is { } captured && captured < clock.GetUtcNow() ? captured : clock.GetUtcNow();
        var added = pod.AddEvidence(
            form.Type, key, Path.GetFileName(file.FileName) is { Length: > 0 } name ? name[..Math.Min(name.Length, 255)] : $"evidence{extension}", contentType, bytes.Length, hash, at,
            new GeoFix(form.Latitude, form.Longitude, form.AccuracyM), form.DeviceReference, user.UserId, width, height, string.Join(" ", warnings), clientId);
        if (added.IsFailure)
        {
            return added.Error;
        }

        await engine.RefreshAsync(pod, cancellationToken);
        memory.Position = 0;
        await files.SaveAsync(key, memory, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        return DeliveryMapper.ToDto(added.Value);
    }

    public Task<Result<PodDto>> RemoveEvidenceAsync(Guid id, Guid evidenceId, string reason, CancellationToken cancellationToken) =>
        MutateAsync(id, async (pod, _) =>
        {
            var removed = pod.RemoveEvidence(evidenceId, reason, clock.GetUtcNow());
            if (removed.IsSuccess)
            {
                await engine.RefreshAsync(pod, cancellationToken);
            }

            return removed;
        }, cancellationToken);

    public async Task<Result<SignatureDto>> AddSignatureAsync(Guid id, UploadSignatureForm form, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: true, write: true, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        if (form.File is not { Length: > 0 } file)
        {
            return Error.Validation("pods.signature_required", "Capture the signature.");
        }

        if (file.Length > 1024 * 1024)
        {
            return Error.Validation("pods.file_too_large", "The signature image is too large.");
        }

        await using var upload = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await upload.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (FileSniffer.Identify(bytes.AsSpan(0, Math.Min(bytes.Length, 8))) is not ("image/png", _))
        {
            return Error.Validation("pods.signature_type", "The signature must be a PNG image.");
        }

        if (!SignatureInk.HasInk(bytes))
        {
            return Error.Validation("pods.signature_blank", "The signature is blank. Ask the recipient to sign.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (pod.Signatures.FirstOrDefault(s => s.FileHash == hash) is { } existing)
        {
            return new SignatureDto(existing.Id, existing.SignerName, existing.SignerDesignation, existing.CapturedAt, existing.Latitude, existing.Longitude, existing.VerificationMethod);
        }

        var signer = Clean(form.SignerName) ?? pod.RecipientName;
        var key = $"pods/{clock.GetUtcNow():yyyy}/{clock.GetUtcNow():MM}/{delivery.Id}/{pod.Id}/signature-{Guid.CreateVersion7()}.png";
        var at = form.CapturedAt is { } captured && captured < clock.GetUtcNow() ? captured : clock.GetUtcNow();
        var added = pod.AddSignature(signer ?? string.Empty, form.SignerDesignation, key, hash, at, new GeoFix(form.Latitude, form.Longitude, null));
        if (added.IsFailure)
        {
            return added.Error;
        }

        if (pod.Method is null)
        {
            pod.SetProof(ProofMethod.Signature, pod.RecipientName ?? signer, pod.RecipientDesignation, pod.RecipientPhone, pod.RecipientRemarks, pod.DriverConfirmed);
        }

        await engine.RefreshAsync(pod, cancellationToken);
        memory.Position = 0;
        await files.SaveAsync(key, memory, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        var saved = pod.Signatures.First(s => s.FileHash == hash);
        return new SignatureDto(saved.Id, saved.SignerName, saved.SignerDesignation, saved.CapturedAt, saved.Latitude, saved.Longitude, saved.VerificationMethod);
    }

    // ---- submitting

    /// <summary>Submits a complete proof (or resubmits one that was sent back). It is then checked, read if a paper POD came with it, and accepted or sent to a reviewer.</summary>
    public Task<Result<PodDto>> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        MutateAsync(id, async (pod, delivery) =>
        {
            var requirements = await engine.RequirementsAsync(pod, cancellationToken);
            var submitted = pod.Submit(requirements, clock.GetUtcNow());
            if (submitted.IsFailure)
            {
                return submitted;
            }

            pod.RaiseSubmitted(delivery, clock.GetUtcNow());

            var ocrRules = await settings.GetAsync<OcrSetting>(DeliverySettingKeys.Ocr, cancellationToken);
            var document = pod.ActiveEvidence.Where(e => e.EvidenceType == EvidenceType.PodDocument).OrderByDescending(e => e.CapturedAt).FirstOrDefault();
            if (ocrRules.Enabled && document is not null && !pod.OcrResults.Any(o => o.EvidenceId == document.Id && o.ProcessingStatus != OcrStatus.Failed))
            {
                QueueOcr(pod, document);
            }

            await engine.DecideAsync(pod, delivery, cancellationToken);
            return Result.Success();
        }, cancellationToken, reviewerMayAct: false);

    public async Task<Result<PodDto>> ValidateAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: true, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        await engine.ValidateAsync(pod, delivery, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(pod, delivery, cancellationToken);
    }

    // ---- reviewing

    public async Task<Result<PodDto>> DecideAsync(Guid id, ReviewPodRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanReview)
        {
            return DeliveryAccess.Forbidden;
        }

        var loaded = await LoadAsync(id, tracked: true, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var now = clock.GetUtcNow();
        var action = request.Action.Trim().ToLowerInvariant();
        Result result;
        switch (action)
        {
            case ReviewActions.Accept:
                await engine.ValidateAsync(pod, delivery, cancellationToken);
                result = pod.Accept(user.UserId, false, now, request.Reason);
                if (result.IsSuccess)
                {
                    await engine.AcceptedAsync(pod, delivery, cancellationToken);
                }

                break;
            case ReviewActions.Reject:
                result = pod.Reject(request.Reason ?? string.Empty, user.UserId, now);
                if (result.IsSuccess)
                {
                    pod.RaiseRejected(delivery, request.Reason!.Trim(), now);
                    await notifications.PublishAsync(Domain.NotificationKind.PodRejected, $"Proof rejected: {delivery.Number}", request.Reason!.Trim(), delivery.Id, pod.Id, null, delivery.TransporterId, null, $"rejected:{pod.Id}:{pod.RejectionCount}", cancellationToken);
                    await exceptions.RaiseAsync(delivery, pod.Id, ExceptionType.PodRejected, $"The proof for {delivery.Number} was rejected: {request.Reason!.Trim()}", cancellationToken);
                }

                break;
            default:
                result = pod.RequestResubmission(request.Reason ?? string.Empty, user.UserId, now);
                if (result.IsSuccess)
                {
                    await notifications.PublishAsync(Domain.NotificationKind.PodRejected, $"More evidence needed: {delivery.Number}", request.Reason!.Trim(), delivery.Id, pod.Id, null, delivery.TransporterId, null, $"resubmission:{pod.Id}:{pod.RejectionCount}", cancellationToken);
                }

                break;
        }

        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(pod, delivery, cancellationToken);
    }

    /// <summary>An accepted proof is never edited: this starts a new version, copying what was there, and the accepted one stays as history.</summary>
    public async Task<Result<PodDto>> RequestCorrectionAsync(Guid id, RequestCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanReview)
        {
            return DeliveryAccess.Forbidden;
        }

        var loaded = await LoadAsync(id, tracked: true, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var next = pod.StartCorrection(delivery, request.Reason, user.UserId, clock.GetUtcNow());
        if (next.IsFailure)
        {
            return next.Error;
        }

        db.Pods.Add(next.Value);
        await engine.RefreshAsync(next.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(next.Value, delivery, cancellationToken);
    }

    // ---- reading the paper document

    public async Task<Result<OcrResultDto>> QueueOcrAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: true, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var document = pod.ActiveEvidence.Where(e => e.EvidenceType == EvidenceType.PodDocument).OrderByDescending(e => e.CapturedAt).FirstOrDefault();
        if (document is null)
        {
            return Error.Conflict("pods.no_document", "Upload the paper POD first.");
        }

        var queued = QueueOcr(pod, document);
        await db.SaveChangesAsync(cancellationToken);
        await FlushOcrJobsAsync(cancellationToken);
        return DeliveryMapper.ToDto(queued, delivery, pod, await settings.GetAsync<OcrSetting>(DeliverySettingKeys.Ocr, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<OcrResultDto>>> OcrAsync(Guid id, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(id, tracked: false, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var rules = await settings.GetAsync<OcrSetting>(DeliverySettingKeys.Ocr, cancellationToken);
        return pod.OcrResults.OrderBy(o => o.QueuedAt).Select(o => DeliveryMapper.ToDto(o, delivery, pod, rules)).ToList();
    }

    /// <summary>A reviewer corrects a value read from the paper. The reading itself is untouched; the correction and the reason are recorded.</summary>
    public async Task<Result<PodDto>> ReviewOcrFieldAsync(Guid id, ReviewOcrFieldRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanReview)
        {
            return DeliveryAccess.Forbidden;
        }

        var loaded = await LoadAsync(id, tracked: true, write: false, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var latest = pod.OcrResults.Where(o => o.ProcessingStatus == OcrStatus.Completed).OrderByDescending(o => o.QueuedAt).FirstOrDefault();
        if (latest is null)
        {
            return Error.Conflict("ocr.nothing_read", "No document has been read for this proof.");
        }

        var old = latest.Field(request.Field)?.EffectiveValue;
        var edited = latest.ReviewField(request.Field, request.Value, user.UserId, clock.GetUtcNow());
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        pod.RecordFieldEdit(edited.Value.FieldName, old, edited.Value.EffectiveValue, request.Reason, user.UserId, clock.GetUtcNow());
        if (pod.Status is PodStatus.Submitted or PodStatus.UnderReview)
        {
            await engine.ValidateAsync(pod, delivery, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(pod, delivery, cancellationToken);
    }

    // ---- plumbing

    /// <summary>Records that the document is to be read. The job is handed to the worker only after the row is saved, so the worker always finds it.</summary>
    private PodOcrResult QueueOcr(PodRecord pod, PodEvidence document)
    {
        var queued = pod.QueueOcr(document, ocr.Provider, clock.GetUtcNow());
        _pending.Add(new PodOcrJob(pod.TenantId, user.UserId, queued.Id));
        return queued;
    }

    private readonly List<PodOcrJob> _pending = [];

    private async Task FlushOcrJobsAsync(CancellationToken cancellationToken)
    {
        foreach (var job in _pending)
        {
            await ocrQueue.EnqueueAsync(job, cancellationToken);
        }

        _pending.Clear();
    }

    private async Task<Result<PodDto>> MutateAsync(
        Guid id, Func<PodRecord, Delivery, Task<Result>> act, CancellationToken cancellationToken, bool reviewerMayAct = true)
    {
        var loaded = await LoadAsync(id, tracked: true, write: true, cancellationToken, reviewerMayAct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (pod, delivery) = loaded.Value;
        var result = await act(pod, delivery);
        if (result.IsFailure)
        {
            db.ChangeTracker.Clear();
            _pending.Clear();
            return result.Error;
        }

        await engine.RefreshAsync(pod, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await FlushOcrJobsAsync(cancellationToken);
        return await mapper.ToDtoAsync(pod, delivery, cancellationToken);
    }

    private async Task<Result<(PodRecord Pod, Delivery Delivery)>> LoadAsync(Guid id, bool tracked, bool write, CancellationToken cancellationToken, bool reviewerMayAct = true)
    {
        var query = db.Pods.WithAll().AsSplitQuery();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var pod = await query.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pod is null)
        {
            return DeliveryAccess.PodNotFound;
        }

        var delivery = await (tracked ? db.Deliveries : db.Deliveries.AsNoTracking()).Include(d => d.Items).Include(d => d.Discrepancies).AsSplitQuery().FirstOrDefaultAsync(d => d.Id == pod.DeliveryId, cancellationToken);
        if (delivery is null || !access.CanSee(delivery))
        {
            return DeliveryAccess.PodNotFound;
        }

        if (write && !(access.CanExecute || (reviewerMayAct && access.CanReview)))
        {
            return DeliveryAccess.Forbidden;
        }

        return (pod, delivery);
    }

    private async Task<bool> CanSeePodAsync(Guid podId, CancellationToken cancellationToken)
    {
        var owner = await db.Pods.AsNoTracking().Where(p => p.Id == podId).Join(db.Deliveries.AsNoTracking(), p => p.DeliveryId, d => d.Id, (p, d) => new { d.TransporterId }).FirstOrDefaultAsync(cancellationToken);
        return owner is not null && access.CanSeeTransporter(owner.TransporterId);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
