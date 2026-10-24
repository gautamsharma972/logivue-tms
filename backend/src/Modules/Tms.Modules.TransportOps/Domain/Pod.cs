using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Domain;

/// <summary>One uploaded POD file. Every file is kept, including those of a rejected submission.</summary>
public sealed class PodDocument : Entity, ITenantScoped
{
    private PodDocument()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public int Submission { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = null!;

    public PodSource Source { get; private set; }

    public Guid? UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public string? Remarks { get; private set; }

    public string? TokenRef { get; private set; }

    public string? ClientKey { get; private set; }

    public static PodDocument Create(Guid tenantId, Guid podId, int submission, string key, string name, string contentType, long size, string sha256, PodSource source, Guid? by, DateTimeOffset at,
        string? remarks, string? tokenRef, string? clientKey) => new()
    {
        TenantId = tenantId, PodId = podId, Submission = submission, FileKey = key, FileName = name, ContentType = contentType, SizeBytes = size, Sha256 = sha256, Source = source, UploadedBy = by,
        UploadedAt = at, Remarks = Customer.Clean(remarks), TokenRef = tokenRef, ClientKey = clientKey,
    };
}

public sealed class PodReview : Entity, ITenantScoped
{
    private PodReview()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public string Action { get; private set; } = null!;

    public Guid? ReviewerId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string? Reason { get; private set; }

    public static PodReview Create(Guid tenantId, Guid podId, string action, Guid? reviewer, DateTimeOffset at, string? reason) =>
        new() { TenantId = tenantId, PodId = podId, Action = action, ReviewerId = reviewer, At = at, Reason = Customer.Clean(reason) };
}

/// <summary>
/// The proof of delivery of one LR. Its status is separate from the delivery status: a delivery can be complete while its POD is still pending. A received POD is never accepted
/// by itself, and a rejected one keeps its files when a corrected one arrives.
/// </summary>
public sealed class Pod : AggregateRoot, ITenantScoped
{
    private Pod()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid LrId { get; private set; }

    public PodStatus Status { get; private set; } = PodStatus.NotYetDue;

    public int Submission { get; private set; }

    public DateTimeOffset? PendingAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public Guid? ReviewerId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public List<PodDocument> Documents { get; private set; } = [];

    public List<PodReview> Reviews { get; private set; } = [];

    public static Pod Create(Guid tenantId, Guid lrId) => new() { TenantId = tenantId, LrId = lrId };

    /// <summary>Delivery was completed but the driver has no POD yet.</summary>
    public Result MarkPending(DateTimeOffset at)
    {
        if (Status is PodStatus.Accepted or PodStatus.Received or PodStatus.UnderReview)
        {
            return Error.Conflict("ops.pod_state", "A POD has already been received for this LR.");
        }

        Status = PodStatus.Pending;
        PendingAt ??= at;
        return Result.Success();
    }

    public Result AddDocuments(IReadOnlyList<PodDocument> documents, DateTimeOffset at)
    {
        if (Status == PodStatus.Accepted)
        {
            return Error.Conflict("ops.pod_accepted", "This POD has been accepted and cannot be changed. Ask the reviewer to reopen it if a correction is needed.");
        }

        Documents.AddRange(documents);
        Status = PodStatus.Received;
        ReceivedAt = at;
        RejectionReason = null;
        return Result.Success();
    }

    /// <summary>The submission number the next upload carries: a new one after a pending or rejected POD, the same one while the current submission is still being reviewed.</summary>
    public int BeginSubmission()
    {
        if (Submission == 0 || Status is PodStatus.NotYetDue or PodStatus.Pending or PodStatus.Rejected)
        {
            Submission++;
        }

        return Submission;
    }

    public Result StartReview(Guid? reviewer, DateTimeOffset at)
    {
        if (Status != PodStatus.Received)
        {
            return Error.Conflict("ops.pod_state", "Only a received POD can be taken into review.");
        }

        Status = PodStatus.UnderReview;
        Reviews.Add(PodReview.Create(TenantId, Id, "StartReview", reviewer, at, null));
        return Result.Success();
    }

    public Result Accept(Guid? reviewer, DateTimeOffset at, string? remarks)
    {
        if (Status is not (PodStatus.Received or PodStatus.UnderReview))
        {
            return Error.Conflict("ops.pod_state", "Only a received POD can be accepted.");
        }

        Status = PodStatus.Accepted;
        ReviewerId = reviewer;
        ReviewedAt = at;
        RejectionReason = null;
        Reviews.Add(PodReview.Create(TenantId, Id, "Accept", reviewer, at, remarks));
        return Result.Success();
    }

    public Result Reject(Guid? reviewer, DateTimeOffset at, string reason)
    {
        if (Status is not (PodStatus.Received or PodStatus.UnderReview))
        {
            return Error.Conflict("ops.pod_state", "Only a received POD can be rejected.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("ops.pod_reason", "A reason is required to reject a POD.");
        }

        Status = PodStatus.Rejected;
        ReviewerId = reviewer;
        ReviewedAt = at;
        RejectionReason = reason.Trim();
        Reviews.Add(PodReview.Create(TenantId, Id, "Reject", reviewer, at, reason));
        return Result.Success();
    }

    /// <summary>A reviewer reopens an accepted POD (for example it was accepted by mistake) before anything has been billed on it.</summary>
    public Result Reopen(Guid? reviewer, DateTimeOffset at, string reason)
    {
        if (Status != PodStatus.Accepted)
        {
            return Error.Conflict("ops.pod_state", "Only an accepted POD can be reopened.");
        }

        Status = PodStatus.UnderReview;
        Reviews.Add(PodReview.Create(TenantId, Id, "Reopen", reviewer, at, reason));
        return Result.Success();
    }
}
