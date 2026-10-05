using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Domain;

public sealed class ExceptionNote : Entity, ITenantScoped
{
    private ExceptionNote()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ExceptionId { get; private set; }

    public string Text { get; private set; } = null!;

    public Guid? By { get; private set; }

    public DateTimeOffset At { get; private set; }

    internal static ExceptionNote Create(Guid tenantId, Guid exceptionId, string text, Guid? by, DateTimeOffset at) => new() { TenantId = tenantId, ExceptionId = exceptionId, Text = text.Trim(), By = by, At = at };
}

/// <summary>A file kept with an exception: a photo of the damage, a customer's email, a weighbridge slip. The file itself lives in the private file store.</summary>
public sealed class ExceptionAttachment : Entity, ITenantScoped
{
    private ExceptionAttachment()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ExceptionId { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string FileHash { get; private set; } = null!;

    public string? Note { get; private set; }

    public Guid? By { get; private set; }

    public DateTimeOffset At { get; private set; }

    internal static ExceptionAttachment Create(Guid tenantId, Guid exceptionId, string key, string name, string contentType, long size, string hash, string? note, Guid? by, DateTimeOffset at) =>
        new() { TenantId = tenantId, ExceptionId = exceptionId, FileKey = key, FileName = name, ContentType = contentType, SizeBytes = size, FileHash = hash, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), By = by, At = at };
}

/// <summary>
/// Something that went wrong with a delivery or its proof, with an owner, a due date and an outcome. It does not disappear when the proof is accepted or the delivery
/// is closed: it ends when someone resolves it.
/// </summary>
public sealed class DeliveryException : AggregateRoot, ITenantScoped
{
    private readonly List<ExceptionNote> _notes = [];
    private readonly List<ExceptionAttachment> _attachments = [];

    private DeliveryException()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid DeliveryId { get; private set; }

    public string DeliveryNumber { get; private set; } = null!;

    public Guid? PodId { get; private set; }

    public Guid? TransporterId { get; private set; }

    public ExceptionType ExceptionType { get; private set; }

    public ExceptionSeverity Severity { get; private set; }

    public ExceptionStatus Status { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public string? Department { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public string? RootCause { get; private set; }

    public ResponsibleParty ResponsibleParty { get; private set; }

    public string Description { get; private set; } = null!;

    public string? ActionTaken { get; private set; }

    public string? Resolution { get; private set; }

    public decimal? FinancialImpact { get; private set; }

    public string? ClaimReference { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    public IReadOnlyList<ExceptionNote> Notes => _notes;

    public IReadOnlyList<ExceptionAttachment> Attachments => _attachments;

    public bool IsOpen => Status is not (ExceptionStatus.Resolved or ExceptionStatus.Closed);

    public static DeliveryException Raise(
        Guid tenantId, string number, Delivery delivery, Guid? podId, ExceptionType type, ExceptionSeverity severity, string description, DateTimeOffset dueAt, DateTimeOffset now)
    {
        var exception = new DeliveryException
        {
            TenantId = tenantId, Number = number, DeliveryId = delivery.Id, DeliveryNumber = delivery.Number, PodId = podId, TransporterId = delivery.TransporterId, ExceptionType = type,
            Severity = severity, Status = ExceptionStatus.Open, Description = description.Trim(), DueAt = dueAt, RaisedAt = now,
        };
        exception.Raise(new DeliveryExceptionRaised(exception.Id, delivery.Id, tenantId, delivery.Number, delivery.TransporterId, type.ToString(), severity.ToString()));
        return exception;
    }

    public Result Acknowledge(Guid? by, DateTimeOffset now)
    {
        if (Status != ExceptionStatus.Open && Status != ExceptionStatus.Escalated)
        {
            return Conflict("acknowledge");
        }

        Status = ExceptionStatus.Acknowledged;
        OwnerUserId ??= by;
        _notes.Add(ExceptionNote.Create(TenantId, Id, "Acknowledged.", by, now));
        return Result.Success();
    }

    public Result Assign(Guid? owner, string? department, DateTimeOffset? dueAt, ExceptionSeverity? severity, Guid? by, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Conflict("assign");
        }

        if (owner is null && string.IsNullOrWhiteSpace(department))
        {
            return Error.Validation("exceptions.owner_required", "Choose a person or a department to own this.");
        }

        OwnerUserId = owner;
        Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        DueAt = dueAt ?? DueAt;
        Severity = severity ?? Severity;
        if (Status == ExceptionStatus.Open)
        {
            Status = ExceptionStatus.Acknowledged;
        }

        _notes.Add(ExceptionNote.Create(TenantId, Id, $"Assigned{(Department is null ? string.Empty : $" to {Department}")}.", by, now));
        return Result.Success();
    }

    public Result Investigate(string? rootCause, ResponsibleParty? party, Guid? by, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Conflict("investigate");
        }

        Status = ExceptionStatus.UnderInvestigation;
        RootCause = string.IsNullOrWhiteSpace(rootCause) ? RootCause : rootCause.Trim();
        ResponsibleParty = party ?? ResponsibleParty;
        _notes.Add(ExceptionNote.Create(TenantId, Id, "Under investigation.", by, now));
        return Result.Success();
    }

    public Result Escalate(string reason, Guid? by, DateTimeOffset now)
    {
        if (!IsOpen || Status == ExceptionStatus.Escalated)
        {
            return Conflict("escalate");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("exceptions.reason_required", "Say why it is being escalated.");
        }

        Status = ExceptionStatus.Escalated;
        EscalatedAt = now;
        Severity = Severity < ExceptionSeverity.Critical ? Severity + 1 : Severity;
        _notes.Add(ExceptionNote.Create(TenantId, Id, $"Escalated: {reason.Trim()}", by, now));
        return Result.Success();
    }

    /// <summary>Keeps a file with the exception. The same file is kept once. Allowed while the exception is open, and noted in its history.</summary>
    public Result<ExceptionAttachment> Attach(string key, string name, string contentType, long size, string hash, string? note, Guid? by, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Conflict("attach a file to");
        }

        if (_attachments.Any(a => a.FileHash == hash))
        {
            return Error.Conflict("exceptions.duplicate_file", "That exact file is already attached.");
        }

        var attachment = ExceptionAttachment.Create(TenantId, Id, key, name, contentType, size, hash, note, by, now);
        _attachments.Add(attachment);
        _notes.Add(ExceptionNote.Create(TenantId, Id, $"Attached {name}.", by, now));
        return attachment;
    }

    public Result AddNote(string text, Guid? by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Error.Validation("exceptions.note_required", "Write the note.");
        }

        _notes.Add(ExceptionNote.Create(TenantId, Id, text, by, now));
        return Result.Success();
    }

    /// <summary>Resolves with a finding. The responsible party is what was found, never assumed from the type of exception.</summary>
    public Result Resolve(string resolution, string? rootCause, ResponsibleParty party, string? actionTaken, decimal? financialImpact, string? claimReference, Guid? by, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Conflict("resolve");
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            return Error.Validation("exceptions.resolution_required", "Say how it was resolved.");
        }

        Status = ExceptionStatus.Resolved;
        Resolution = resolution.Trim();
        RootCause = string.IsNullOrWhiteSpace(rootCause) ? RootCause : rootCause.Trim();
        ResponsibleParty = party;
        ActionTaken = string.IsNullOrWhiteSpace(actionTaken) ? ActionTaken : actionTaken.Trim();
        FinancialImpact = financialImpact;
        ClaimReference = string.IsNullOrWhiteSpace(claimReference) ? ClaimReference : claimReference.Trim();
        ResolvedAt = now;
        _notes.Add(ExceptionNote.Create(TenantId, Id, $"Resolved: {Resolution}", by, now));
        Raise(new DeliveryExceptionResolved(Id, DeliveryId, TenantId, DeliveryNumber, TransporterId, ExceptionType.ToString(), party.ToString(), financialImpact, now));
        return Result.Success();
    }

    public Result Close(Guid? by, DateTimeOffset now)
    {
        if (Status != ExceptionStatus.Resolved)
        {
            return Error.Conflict("exceptions.not_resolved", "Resolve the exception before closing it.");
        }

        Status = ExceptionStatus.Closed;
        _notes.Add(ExceptionNote.Create(TenantId, Id, "Closed.", by, now));
        return Result.Success();
    }

    public void LinkClaim(string reference) => ClaimReference = reference;

    private Error Conflict(string action) => Error.Conflict("exceptions.invalid_state", $"A {Status} exception cannot be {action}d.");
}
