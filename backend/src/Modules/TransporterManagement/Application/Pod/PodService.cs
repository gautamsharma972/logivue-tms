using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Pod;
using Microsoft.Extensions.Logging;
using Severity = LogiVue.Tms.TransporterManagement.Domain.Common.Severity;

namespace LogiVue.Tms.TransporterManagement.Application.Pod;

/// <summary>
/// POD lifecycle: Pending (created on delivery) → Submitted → UnderReview → Accepted, or Rejected → ResubmissionRequired.
/// A scope argument is set for vendor callers, who see only their own PODs.
/// </summary>
public interface IPodService
{
    Task<PodDto> SubmitAsync(string loadReference, long transporterId, PodSubmissionRequest request, Stream content, CancellationToken cancellationToken = default);

    Task<PodDto> GetAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<PagedResult<PodDto>> ListAsync(long? transporterId, PodStatus? status, long? scopeTransporterId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);

    Task<PodFileDto> OpenFileAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<PodDto> StartReviewAsync(long id, CancellationToken cancellationToken = default);

    Task<PodDto> AcceptAsync(long id, CancellationToken cancellationToken = default);

    Task<PodDto> RejectAsync(long id, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<PodDto> RequestResubmissionAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class PodService(
    IRepository<PodRecord> pods,
    IRepository<TransporterAlert> alerts,
    IDocumentStorage storage,
    IPerformanceService performance,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<PodSubmissionRequest> submissionValidator,
    IValidator<ReasonRequest> reasonValidator,
    ILogger<PodService> logger) : IPodService
{
    public async Task<PodDto> SubmitAsync(string loadReference, long transporterId, PodSubmissionRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        await submissionValidator.ValidateAndThrowAsync(request, cancellationToken);

        var load = loadReference.Trim();
        var pod = (await pods.ListAsync(p => p.LoadReference == load && p.TransporterId == transporterId, cancellationToken))
            .OrderByDescending(p => p.Id)
            .FirstOrDefault()
            ?? throw new NotFoundException($"No delivery is recorded for load {load}, so no POD is expected.", "POD_NOT_EXPECTED");

        if (pod.Status is not (PodStatus.Pending or PodStatus.ResubmissionRequired))
        {
            throw new BusinessRuleException($"A POD for load {load} cannot be submitted while it is {pod.Status}.", "POD_NOT_SUBMITTABLE");
        }

        var stored = await storage.SaveAsync(content, request.FileName, cancellationToken);
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

            pod.Status = PodStatus.Submitted;
            pod.SubmittedAt = now;
            pod.SubmittedWithinSla = now <= pod.DueAt;
            pod.PodDate = request.PodDate;
            pod.ReceivedBy = request.ReceivedBy.Trim();
            pod.FileReference = stored.FileReference;
            pod.OriginalFileName = request.FileName;
            pod.ContentType = request.ContentType;
            pod.FileSizeBytes = stored.SizeBytes;
            pod.UpdatedAt = now;

            await audit.RecordAsync(new AuditEntry("PodRecord", pod.Id.ToString(), "PodSubmitted",
                NewValueJson: AuditJson.Serialize(new { pod.SubmittedWithinSla, request.FileName, stored.SizeBytes })), cancellationToken);
            await performance.RefreshAsync(pod.TransporterId, [pod.DeliveredAt], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // The file is useless without the record that points to it, so it is removed when the change does not commit.
            await storage.DeleteAsync(stored.FileReference, CancellationToken.None);
            throw;
        }

        logger.LogInformation("POD {PodId} submitted for load {LoadReference} (within SLA: {WithinSla})", pod.Id, load, pod.SubmittedWithinSla);
        return ToDto(pod, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<PodDto> GetAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default) =>
        ToDto(await LoadAsync(id, scopeTransporterId, cancellationToken), clock.GetUtcNow().UtcDateTime);

    public async Task<PagedResult<PodDto>> ListAsync(long? transporterId, PodStatus? status, long? scopeTransporterId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var owner = scopeTransporterId ?? transporterId;
        var normalisedPage = Paging.NormalisePage(page);
        var size = Paging.NormalisePageSize(pageSize);
        var rows = await pods.PageAsync(p => (owner == null || p.TransporterId == owner) && (status == null || p.Status == status),
            normalisedPage, size, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        return new PagedResult<PodDto>(rows.Items.Select(p => ToDto(p, now)).ToList(), normalisedPage, size, rows.TotalCount);
    }

    public async Task<PodFileDto> OpenFileAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default)
    {
        var pod = await LoadAsync(id, scopeTransporterId, cancellationToken);
        if (pod.FileReference is null)
        {
            throw new NotFoundException($"POD {id} has no file yet.", "POD_FILE_NOT_FOUND");
        }

        var stream = await storage.OpenReadAsync(pod.FileReference, cancellationToken);
        return new PodFileDto(stream, pod.OriginalFileName ?? $"pod-{id}", pod.ContentType ?? "application/octet-stream");
    }

    public async Task<PodDto> StartReviewAsync(long id, CancellationToken cancellationToken = default)
    {
        var pod = await LoadAsync(id, null, cancellationToken);
        if (pod.Status != PodStatus.Submitted)
        {
            throw Illegal(pod, "reviewed");
        }

        pod.Status = PodStatus.UnderReview;
        pod.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(pod, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<PodDto> AcceptAsync(long id, CancellationToken cancellationToken = default)
    {
        var pod = await LoadAsync(id, null, cancellationToken);
        if (pod.Status is not (PodStatus.Submitted or PodStatus.UnderReview))
        {
            throw Illegal(pod, "accepted");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        pod.Status = PodStatus.Accepted;
        pod.ReviewedBy = currentUser.UserId;
        pod.ReviewedAt = now;
        pod.UpdatedAt = now;
        await audit.RecordAsync(new AuditEntry("PodRecord", id.ToString(), "PodAccepted"), cancellationToken);

        await performance.RefreshAsync(pod.TransporterId, [pod.DeliveredAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(pod, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<PodDto> RejectAsync(long id, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var pod = await LoadAsync(id, null, cancellationToken);
        if (pod.Status is not (PodStatus.Submitted or PodStatus.UnderReview))
        {
            throw Illegal(pod, "rejected");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var reason = string.IsNullOrWhiteSpace(request.Comments) ? request.Reason.Trim() : $"{request.Reason.Trim()}: {request.Comments.Trim()}";
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        pod.Status = PodStatus.Rejected;
        pod.RejectionCount++;
        pod.RejectionReason = reason;
        pod.ReviewedBy = currentUser.UserId;
        pod.ReviewedAt = now;
        pod.UpdatedAt = now;
        alerts.Add(new TransporterAlert
        {
            AlertType = "POD_REJECTED",
            Severity = Severity.Medium,
            TransporterId = pod.TransporterId,
            LoadReference = pod.LoadReference,
            EntityType = "PodRecord",
            EntityId = id.ToString(),
            Message = $"POD for load {pod.LoadReference} was rejected: {reason}",
            CreatedAt = now,
            Status = AlertStatus.Open
        });
        await audit.RecordAsync(new AuditEntry("PodRecord", id.ToString(), "PodRejected", Reason: reason), cancellationToken);

        await performance.RefreshAsync(pod.TransporterId, [pod.DeliveredAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(pod, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<PodDto> RequestResubmissionAsync(long id, CancellationToken cancellationToken = default)
    {
        var pod = await LoadAsync(id, null, cancellationToken);
        if (pod.Status != PodStatus.Rejected)
        {
            throw Illegal(pod, "sent back for resubmission");
        }

        pod.Status = PodStatus.ResubmissionRequired;
        pod.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await audit.RecordAsync(new AuditEntry("PodRecord", id.ToString(), "PodResubmissionRequested"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(pod, clock.GetUtcNow().UtcDateTime);
    }

    private async Task<PodRecord> LoadAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken)
    {
        var pod = await pods.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException($"POD {id} was not found.", "POD_NOT_FOUND");
        if (scopeTransporterId is { } scope && pod.TransporterId != scope)
        {
            throw new NotFoundException($"POD {id} was not found.", "POD_NOT_FOUND");
        }

        return pod;
    }

    private static BusinessRuleException Illegal(PodRecord pod, string action) =>
        new($"POD {pod.Id} cannot be {action} while it is {pod.Status}.", "POD_ILLEGAL_TRANSITION");

    private static PodDto ToDto(PodRecord pod, DateTime now)
    {
        var sla = pod.SubmittedAt is null
            ? (now > pod.DueAt ? "Overdue" : "Pending")
            : (pod.SubmittedWithinSla == true ? "OnTime" : "Late");

        return new PodDto(pod.Id, pod.LoadReference, pod.TransporterId, pod.LoadExecutionId, pod.DeliveredAt, pod.DueAt,
            pod.SubmittedAt, pod.SubmittedWithinSla, sla, pod.PodDate, pod.ReceivedBy, pod.OriginalFileName, pod.Status,
            pod.RejectionCount, pod.RejectionReason, pod.ReviewedBy, pod.ReviewedAt);
    }
}
