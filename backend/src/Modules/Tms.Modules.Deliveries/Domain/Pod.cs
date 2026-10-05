using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Domain;

/// <summary>A photo or scan that supports a proof of delivery. Never overwritten: removing one only marks it removed, with a reason.</summary>
public sealed class PodEvidence : Entity, ITenantScoped
{
    private PodEvidence()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public EvidenceType EvidenceType { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string FileHash { get; private set; } = null!;

    public DateTimeOffset CapturedAt { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? DeviceReference { get; private set; }

    public Guid? CapturedBy { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    /// <summary>Quality problems noticed on upload (low resolution…), kept so a reviewer sees them.</summary>
    public string? Warnings { get; private set; }

    /// <summary>The mobile app's key for this capture: a retry with the same key returns this record instead of adding another.</summary>
    public string? ClientRecordId { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public string? RemovedReason { get; private set; }

    public bool IsActive => RemovedAt is null;

    internal static PodEvidence Create(
        Guid tenantId, Guid podId, EvidenceType type, string fileKey, string fileName, string contentType, long size, string hash, DateTimeOffset capturedAt, GeoFix fix,
        string? device, Guid? by, int? width, int? height, string? warnings, string? clientRecordId) => new()
    {
        TenantId = tenantId, PodId = podId, EvidenceType = type, FileKey = fileKey, FileName = fileName, ContentType = contentType, SizeBytes = size, FileHash = hash, CapturedAt = capturedAt,
        Latitude = fix.IsKnown ? fix.Latitude : null, Longitude = fix.IsKnown ? fix.Longitude : null, DeviceReference = device, CapturedBy = by, Width = width, Height = height,
        Warnings = string.IsNullOrWhiteSpace(warnings) ? null : warnings, ClientRecordId = clientRecordId,
    };

    internal void Remove(string reason, DateTimeOffset now)
    {
        RemovedAt = now;
        RemovedReason = reason;
    }
}

public sealed class PodSignature : Entity, ITenantScoped
{
    private PodSignature()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public string SignerName { get; private set; } = null!;

    public string? SignerDesignation { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileHash { get; private set; } = null!;

    public DateTimeOffset CapturedAt { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string VerificationMethod { get; private set; } = null!;

    internal static PodSignature Create(Guid tenantId, Guid podId, string signer, string? designation, string fileKey, string hash, DateTimeOffset at, GeoFix fix, string method) => new()
    {
        TenantId = tenantId, PodId = podId, SignerName = signer.Trim(), SignerDesignation = string.IsNullOrWhiteSpace(designation) ? null : designation.Trim(), FileKey = fileKey, FileHash = hash,
        CapturedAt = at, Latitude = fix.IsKnown ? fix.Latitude : null, Longitude = fix.IsKnown ? fix.Longitude : null, VerificationMethod = method,
    };
}

/// <summary>The quantities as the proof records them, per delivery line.</summary>
public sealed class PodItem : Entity, ITenantScoped
{
    private PodItem()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public Guid DeliveryItemId { get; private set; }

    public string SkuReference { get; private set; } = null!;

    public decimal OrderedQuantity { get; private set; }

    public decimal DispatchedQuantity { get; private set; }

    public decimal DeliveredQuantity { get; private set; }

    public decimal ShortQuantity { get; private set; }

    public decimal DamagedQuantity { get; private set; }

    public decimal RejectedQuantity { get; private set; }

    public string? Remarks { get; private set; }

    internal static PodItem From(Guid tenantId, Guid podId, DeliveryItem i) => new()
    {
        TenantId = tenantId, PodId = podId, DeliveryItemId = i.Id, SkuReference = i.SkuReference, OrderedQuantity = i.OrderedQuantity, DispatchedQuantity = i.DispatchedQuantity,
        DeliveredQuantity = i.DeliveredQuantity ?? 0, ShortQuantity = i.ShortQuantity, DamagedQuantity = i.DamagedQuantity, RejectedQuantity = i.RejectedQuantity, Remarks = i.Remarks,
    };

    internal PodItem CopyFor(Guid podId) => new()
    {
        TenantId = TenantId, PodId = podId, DeliveryItemId = DeliveryItemId, SkuReference = SkuReference, OrderedQuantity = OrderedQuantity, DispatchedQuantity = DispatchedQuantity,
        DeliveredQuantity = DeliveredQuantity, ShortQuantity = ShortQuantity, DamagedQuantity = DamagedQuantity, RejectedQuantity = RejectedQuantity, Remarks = Remarks,
    };
}

public sealed class PodValidationResult : Entity, ITenantScoped
{
    private PodValidationResult()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    /// <summary>Structural, Quantity, Evidence, Business or Ocr.</summary>
    public string ValidationType { get; private set; } = null!;

    /// <summary>A short stable name for what was checked, e.g. "Signature".</summary>
    public string Check { get; private set; } = null!;

    public ValidationOutcome Status { get; private set; }

    public string Message { get; private set; } = null!;

    public DateTimeOffset ValidatedAt { get; private set; }

    internal static PodValidationResult Create(Guid tenantId, Guid podId, string type, string check, ValidationOutcome status, string message, DateTimeOffset at) => new()
    {
        TenantId = tenantId, PodId = podId, ValidationType = type, Check = check, Status = status, Message = message, ValidatedAt = at,
    };
}

public sealed class PodReviewAction : Entity, ITenantScoped
{
    private PodReviewAction()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public string Action { get; private set; } = null!;

    public string? FieldName { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public string? Reason { get; private set; }

    public Guid? PerformedBy { get; private set; }

    public DateTimeOffset PerformedAt { get; private set; }

    internal static PodReviewAction Create(Guid tenantId, Guid podId, string action, string? field, string? oldValue, string? newValue, string? reason, Guid? by, DateTimeOffset at) => new()
    {
        TenantId = tenantId, PodId = podId, Action = action, FieldName = field, OldValue = oldValue, NewValue = newValue, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), PerformedBy = by, PerformedAt = at,
    };
}

/// <summary>What a proof of delivery still lacks before it can be sent.</summary>
public sealed record PodRequirements(IReadOnlyList<string> Missing)
{
    public bool IsMet => Missing.Count == 0;
}

/// <summary>
/// The evidence package for one delivery: who received what, where, when and how it was confirmed. Versioned: an accepted proof is never edited,
/// a correction is a new version and the old one stays as it was.
/// </summary>
public sealed class PodRecord : AggregateRoot, ITenantScoped
{
    private readonly List<PodItem> _items = [];
    private readonly List<PodEvidence> _evidence = [];
    private readonly List<PodSignature> _signatures = [];
    private readonly List<PodValidationResult> _validations = [];
    private readonly List<PodReviewAction> _reviews = [];
    private readonly List<PodOcrResult> _ocrResults = [];

    private PodRecord()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public string PodNumber { get; private set; } = null!;

    public int PodVersion { get; private set; } = 1;

    /// <summary>The newest version of this proof; earlier ones stay for history.</summary>
    public bool IsCurrent { get; private set; } = true;

    public Guid? SupersedesPodId { get; private set; }

    public PodStatus Status { get; private set; }

    public ProofMethod? Method { get; private set; }

    public string? RecipientName { get; private set; }

    public string? RecipientDesignation { get; private set; }

    public string? RecipientPhone { get; private set; }

    public DateTimeOffset? ArrivalAt { get; private set; }

    public DateTimeOffset? DeliveryCompletedAt { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public DateTimeOffset? FirstSubmittedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>When the proof was last sent back (rejected or more evidence asked for); the resubmission clock runs from here.</summary>
    public DateTimeOffset? ReturnedAt { get; private set; }

    public DateTimeOffset? ResubmittedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public double? GpsAccuracy { get; private set; }

    public GeofenceStatus Geofence { get; private set; }

    public string? DriverRemarks { get; private set; }

    public string? RecipientRemarks { get; private set; }

    /// <summary>The driver confirms a contactless delivery (no recipient present).</summary>
    public bool DriverConfirmed { get; private set; }

    public bool OtpVerified { get; private set; }

    public int OtpAttempts { get; private set; }

    public DateTimeOffset? OtpVerifiedAt { get; private set; }

    public bool CustomerAcknowledged { get; private set; }

    public string? RejectionReason { get; private set; }

    public int RejectionCount { get; private set; }

    public bool AutoAccepted { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public IReadOnlyList<PodItem> Items => _items;

    public IReadOnlyList<PodEvidence> Evidence => _evidence;

    public IReadOnlyList<PodSignature> Signatures => _signatures;

    public IReadOnlyList<PodValidationResult> Validations => _validations;

    public IReadOnlyList<PodReviewAction> Reviews => _reviews;

    public IReadOnlyList<PodOcrResult> OcrResults => _ocrResults;

    public IEnumerable<PodEvidence> ActiveEvidence => _evidence.Where(e => e.IsActive);

    public bool IsEditable => Status is PodStatus.Draft or PodStatus.Captured or PodStatus.Rejected or PodStatus.ResubmissionRequired;

    public static PodRecord Create(Delivery delivery, string number, GeoFix fix, GeofenceStatus geofence, string? driverRemarks, DateTimeOffset now) => Build(delivery, number, 1, null, fix, geofence, driverRemarks, now);

    private static PodRecord Build(Delivery d, string number, int version, Guid? supersedes, GeoFix fix, GeofenceStatus geofence, string? remarks, DateTimeOffset now)
    {
        var pod = new PodRecord
        {
            TenantId = d.TenantId, DeliveryId = d.Id, PodNumber = number, PodVersion = version, SupersedesPodId = supersedes, Status = PodStatus.Draft, ArrivalAt = d.ActualArrivalAt,
            DeliveryCompletedAt = d.ActualDeliveryAt, CapturedAt = now, Latitude = fix.IsKnown ? fix.Latitude : null, Longitude = fix.IsKnown ? fix.Longitude : null,
            GpsAccuracy = fix.IsKnown ? fix.AccuracyM : null, Geofence = geofence, DriverRemarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
            OtpVerified = d.OtpVerified, OtpAttempts = d.OtpAttempts, OtpVerifiedAt = d.OtpVerifiedAt,
        };
        foreach (var item in d.Items)
        {
            pod._items.Add(PodItem.From(d.TenantId, pod.Id, item));
        }

        return pod;
    }

    /// <summary>Who received the goods and how the delivery was confirmed.</summary>
    public Result SetProof(ProofMethod method, string? recipient, string? designation, string? phone, string? recipientRemarks, bool driverConfirmed)
    {
        if (!IsEditable)
        {
            return Error.Conflict("pods.locked", "This proof can no longer be changed. Request a correction.");
        }

        Method = method;
        RecipientName = string.IsNullOrWhiteSpace(recipient) ? null : recipient.Trim();
        RecipientDesignation = string.IsNullOrWhiteSpace(designation) ? null : designation.Trim();
        RecipientPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        RecipientRemarks = string.IsNullOrWhiteSpace(recipientRemarks) ? null : recipientRemarks.Trim();
        DriverConfirmed = driverConfirmed;
        return Result.Success();
    }

    public Result<PodEvidence> AddEvidence(
        EvidenceType type, string fileKey, string fileName, string contentType, long size, string hash, DateTimeOffset capturedAt, GeoFix fix, string? device, Guid? by,
        int? width, int? height, string? warnings, string? clientRecordId)
    {
        if (!IsEditable)
        {
            return Error.Conflict("pods.locked", "This proof can no longer be changed. Request a correction.");
        }

        var evidence = PodEvidence.Create(TenantId, Id, type, fileKey, fileName, contentType, size, hash, capturedAt, fix, device, by, width, height, warnings, clientRecordId);
        _evidence.Add(evidence);
        return evidence;
    }

    public Result RemoveEvidence(Guid evidenceId, string reason, DateTimeOffset now)
    {
        if (!IsEditable)
        {
            return Error.Conflict("pods.locked", "This proof can no longer be changed. Request a correction.");
        }

        var evidence = _evidence.FirstOrDefault(e => e.Id == evidenceId && e.IsActive);
        if (evidence is null)
        {
            return Error.NotFound("pods.evidence_not_found", "Evidence not found.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("pods.reason_required", "Say why the evidence is being removed.");
        }

        evidence.Remove(reason.Trim(), now);
        return Result.Success();
    }

    public Result AddSignature(string signer, string? designation, string fileKey, string hash, DateTimeOffset at, GeoFix fix)
    {
        if (!IsEditable)
        {
            return Error.Conflict("pods.locked", "This proof can no longer be changed. Request a correction.");
        }

        if (string.IsNullOrWhiteSpace(signer))
        {
            return Error.Validation("pods.signer_required", "Enter the name of the person who signed.");
        }

        _signatures.Add(PodSignature.Create(TenantId, Id, signer, designation, fileKey, hash, at, fix, "Drawn"));
        return Result.Success();
    }

    public void AcknowledgeByCustomer() => CustomerAcknowledged = true;

    /// <summary>The delivery's OTP check result is copied onto the proof so the evidence stands on its own.</summary>
    public void CopyOtp(Delivery d)
    {
        OtpVerified = d.OtpVerified;
        OtpAttempts = d.OtpAttempts;
        OtpVerifiedAt = d.OtpVerifiedAt;
    }

    /// <summary>Compares what the rules ask for with what is present. Pure: the rules and the damage list are passed in.</summary>
    public PodRequirements CheckRequirements(PodRulesSetting rules, DiscrepancyRulesSetting discrepancy, IReadOnlyCollection<string> damageTypesNeedingPhoto, DateTimeOffset now)
    {
        var missing = new List<string>();
        var photos = ActiveEvidence.Count(e => e.EvidenceType is not EvidenceType.PodDocument);

        if (string.IsNullOrWhiteSpace(RecipientName))
        {
            missing.Add("The name of the person who received the goods");
        }

        if (Method is null)
        {
            missing.Add("How the delivery was confirmed (signature, code, photo or contactless)");
        }

        if (rules.SignatureRequired && _signatures.Count == 0)
        {
            missing.Add("The recipient's signature");
        }

        if (Method == ProofMethod.Signature && _signatures.Count == 0)
        {
            missing.Add("The recipient's signature");
        }

        if ((rules.OtpRequired || Method == ProofMethod.Otp) && !OtpVerified)
        {
            missing.Add("The customer's one-time code");
        }

        if (Method == ProofMethod.Contactless && (!DriverConfirmed || photos == 0 || !GeoFix.IsKnownValue(Latitude, Longitude)))
        {
            missing.Add("For a contactless delivery: the driver's confirmation, a photo and the location");
        }

        if (rules.GpsRequired && !GeoFix.IsKnownValue(Latitude, Longitude))
        {
            missing.Add("The location of the delivery (GPS)");
        }

        if (rules.PhotoRequired && photos < rules.MinPhotos)
        {
            missing.Add(rules.MinPhotos <= 1 ? "A photo of the delivery" : $"At least {rules.MinPhotos} photos of the delivery");
        }

        if (rules.GeofenceRequired && Geofence is GeofenceStatus.GpsUnavailable or GeofenceStatus.AccuracyInsufficient)
        {
            missing.Add("A usable location fix at the customer");
        }

        var damaged = _items.Where(i => i.DamagedQuantity > 0).ToList();
        if (damaged.Count > 0 && damageTypesNeedingPhoto.Count > 0 && ActiveEvidence.All(e => e.EvidenceType != EvidenceType.DamagePhoto))
        {
            missing.Add("A photo of the damage");
        }

        if (HasShortageOrDamageOrRejection && ((discrepancy.ShortageAcknowledgementRequired && _items.Any(i => i.ShortQuantity > 0)) || (discrepancy.DamageAcknowledgementRequired && damaged.Count > 0)) && !CustomerAcknowledged)
        {
            missing.Add("The customer's acknowledgement of the shortage or damage");
        }

        return new PodRequirements(missing.Distinct().ToList());
    }

    private bool HasShortageOrDamageOrRejection => _items.Any(i => i.ShortQuantity > 0 || i.DamagedQuantity > 0 || i.RejectedQuantity > 0);

    /// <summary>Keeps the Draft / Captured distinction in step with what is present.</summary>
    public void Refresh(PodRequirements requirements)
    {
        if (Status is PodStatus.Draft or PodStatus.Captured)
        {
            Status = requirements.IsMet ? PodStatus.Captured : PodStatus.Draft;
        }
    }

    public Result Submit(PodRequirements requirements, DateTimeOffset now)
    {
        if (!IsEditable)
        {
            return Error.Conflict("pods.not_submittable", $"A proof that is {Status} cannot be submitted.");
        }

        if (!requirements.IsMet)
        {
            return Error.Validation("pods.incomplete", $"The proof is not complete: {string.Join("; ", requirements.Missing)}.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["pod"] = [.. requirements.Missing] },
            };
        }

        if (Status is PodStatus.Rejected or PodStatus.ResubmissionRequired)
        {
            _reviews.Add(PodReviewAction.Create(TenantId, Id, "Resubmitted", null, null, null, null, null, now));
            ResubmittedAt = now;
        }

        Status = PodStatus.Submitted;
        SubmittedAt = now;
        FirstSubmittedAt ??= now;
        RejectionReason = null;
        return Result.Success();
    }

    public void ApplyValidation(IEnumerable<(string Type, string Check, ValidationOutcome Status, string Message)> results, DateTimeOffset now)
    {
        _validations.Clear();
        foreach (var (type, check, status, message) in results)
        {
            _validations.Add(PodValidationResult.Create(TenantId, Id, type, check, status, message, now));
        }
    }

    public ValidationOutcome ValidationSummary => _validations.Count == 0
        ? ValidationOutcome.Valid
        : _validations.Any(v => v.Status == ValidationOutcome.Invalid) ? ValidationOutcome.Invalid
        : _validations.Any(v => v.Status == ValidationOutcome.RequiresReview) ? ValidationOutcome.RequiresReview
        : _validations.Any(v => v.Status == ValidationOutcome.Warning) ? ValidationOutcome.Warning : ValidationOutcome.Valid;

    public void SendToReview(DateTimeOffset now)
    {
        if (Status == PodStatus.Submitted)
        {
            Status = PodStatus.UnderReview;
        }
    }

    /// <summary>Accepts the proof. A proof that fails a mandatory check cannot be accepted: it has to be corrected or rejected.</summary>
    public Result Accept(Guid? reviewer, bool automatic, DateTimeOffset now, string? reason = null)
    {
        if (Status is not (PodStatus.Submitted or PodStatus.UnderReview))
        {
            return Error.Conflict("pods.not_reviewable", $"A proof that is {Status} cannot be accepted.");
        }

        if (ValidationSummary == ValidationOutcome.Invalid)
        {
            return Error.Conflict("pods.validation_failed", "The proof fails a mandatory check and cannot be accepted: " + string.Join(" ", _validations.Where(v => v.Status == ValidationOutcome.Invalid).Select(v => v.Message)));
        }

        Status = PodStatus.Accepted;
        ApprovedAt = now;
        ReviewedAt = now;
        ReviewedBy = reviewer;
        AutoAccepted = automatic;
        _reviews.Add(PodReviewAction.Create(TenantId, Id, automatic ? "AutoAccepted" : "Accepted", null, null, null, reason, reviewer, now));
        return Result.Success();
    }

    public Result Reject(string reason, Guid? reviewer, DateTimeOffset now) => Send(PodStatus.Rejected, "Rejected", reason, reviewer, now);

    public Result RequestResubmission(string reason, Guid? reviewer, DateTimeOffset now) => Send(PodStatus.ResubmissionRequired, "ResubmissionRequested", reason, reviewer, now);

    private Result Send(PodStatus to, string action, string reason, Guid? reviewer, DateTimeOffset now)
    {
        if (Status is not (PodStatus.Submitted or PodStatus.UnderReview))
        {
            return Error.Conflict("pods.not_reviewable", $"A proof that is {Status} cannot be {(to == PodStatus.Rejected ? "rejected" : "sent back")}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("pods.reason_required", "Say why the proof is being sent back.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["reason"] = ["Give a reason."] },
            };
        }

        Status = to;
        RejectionReason = reason.Trim();
        ReturnedAt = now;
        ResubmittedAt = null;
        RejectionCount++;
        ReviewedAt = now;
        ReviewedBy = reviewer;
        _reviews.Add(PodReviewAction.Create(TenantId, Id, action, null, null, null, reason, reviewer, now));
        return Result.Success();
    }

    /// <summary>Records a reviewer's edit of an OCR value. The original reading is kept on the field; this only adds the audit line.</summary>
    public void RecordFieldEdit(string field, string? oldValue, string? newValue, string reason, Guid? by, DateTimeOffset now) =>
        _reviews.Add(PodReviewAction.Create(TenantId, Id, "FieldEdited", field, oldValue, newValue, reason, by, now));

    public void RecordNote(string action, string? reason, Guid? by, DateTimeOffset now) => _reviews.Add(PodReviewAction.Create(TenantId, Id, action, null, null, null, reason, by, now));

    public PodOcrResult QueueOcr(PodEvidence document, string provider, DateTimeOffset now)
    {
        var result = PodOcrResult.Queue(TenantId, Id, document.Id, provider, now);
        _ocrResults.Add(result);
        return result;
    }

    /// <summary>An accepted proof is never edited. A correction starts a new version (copying what was there) and the accepted one stays as history.</summary>
    public Result<PodRecord> StartCorrection(Delivery delivery, string reason, Guid? by, DateTimeOffset now)
    {
        if (Status != PodStatus.Accepted || !IsCurrent)
        {
            return Error.Conflict("pods.not_correctable", "Only the current, accepted proof can be corrected.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("pods.reason_required", "Say what needs correcting.");
        }

        var next = new PodRecord
        {
            TenantId = TenantId, DeliveryId = DeliveryId, PodNumber = PodNumber, PodVersion = PodVersion + 1, SupersedesPodId = Id, Status = PodStatus.Draft, Method = Method,
            RecipientName = RecipientName, RecipientDesignation = RecipientDesignation, RecipientPhone = RecipientPhone, ArrivalAt = ArrivalAt, DeliveryCompletedAt = DeliveryCompletedAt,
            CapturedAt = now, Latitude = Latitude, Longitude = Longitude, GpsAccuracy = GpsAccuracy, Geofence = Geofence, DriverRemarks = DriverRemarks, RecipientRemarks = RecipientRemarks,
            DriverConfirmed = DriverConfirmed, OtpVerified = OtpVerified, OtpAttempts = OtpAttempts, OtpVerifiedAt = OtpVerifiedAt, CustomerAcknowledged = CustomerAcknowledged,
            FirstSubmittedAt = FirstSubmittedAt,
        };
        foreach (var item in _items)
        {
            next._items.Add(item.CopyFor(next.Id));
        }

        foreach (var e in ActiveEvidence)
        {
            next._evidence.Add(PodEvidence.Create(TenantId, next.Id, e.EvidenceType, e.FileKey, e.FileName, e.ContentType, e.SizeBytes, e.FileHash, e.CapturedAt,
                new GeoFix(e.Latitude, e.Longitude, null), e.DeviceReference, e.CapturedBy, e.Width, e.Height, e.Warnings, null));
        }

        foreach (var s in _signatures)
        {
            next._signatures.Add(PodSignature.Create(TenantId, next.Id, s.SignerName, s.SignerDesignation, s.FileKey, s.FileHash, s.CapturedAt, new GeoFix(s.Latitude, s.Longitude, null), s.VerificationMethod));
        }

        IsCurrent = false;
        _reviews.Add(PodReviewAction.Create(TenantId, Id, "CorrectionRequested", null, null, null, reason, by, now));
        next._reviews.Add(PodReviewAction.Create(TenantId, next.Id, "CorrectionStarted", null, null, null, reason, by, now));
        return next;
    }

    public void Cancel()
    {
        Status = PodStatus.Cancelled;
        IsCurrent = false;
    }

    public void RaiseAccepted(Delivery d, DateTimeOffset now, int submissionSlaHours) =>
        Raise(new PodAccepted(
            d.Id, Id, TenantId, d.Number, d.ShipmentId, d.TransporterId, d.ActualDeliveryAt ?? now, FirstSubmittedAt, now, RejectionCount == 0 && PodVersion == 1,
            d.Items.Sum(i => i.ShortQuantity), d.Items.Sum(i => i.DamagedQuantity),
            FirstSubmittedAt is { } first && d.ActualDeliveryAt is { } done ? first - done <= TimeSpan.FromHours(submissionSlaHours) : null, d.OnTime));

    public void RaiseSubmitted(Delivery d, DateTimeOffset now) => Raise(new PodSubmitted(d.Id, Id, TenantId, d.Number, d.ShipmentId, d.TransporterId, now));

    public void RaiseRejected(Delivery d, string reason, DateTimeOffset now) => Raise(new PodRejected(d.Id, Id, TenantId, d.Number, d.TransporterId, reason, now));
}
