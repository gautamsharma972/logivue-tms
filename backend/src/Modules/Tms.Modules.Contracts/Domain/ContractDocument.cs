using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum ContractDocumentKind
{
    SignedContract = 1,
    Annexure = 2,
    Amendment = 3,
    Correspondence = 4,
    Other = 5,
    MasterAgreement = 6,
    RateAnnexure = 7,
    ServiceLevelAgreement = 8,
    DphAnnexure = 9,
    RenewalLetter = 10,
    Insurance = 11,
    CommercialAnnexure = 12,
    SupportingDocument = 13,
}

public enum DocumentStatus
{
    Pending = 1,
    Verified = 2,
}

/// <summary>A file in the contract's repository: the signed agreement, annexures, amendments.</summary>
public sealed class ContractDocument : AggregateRoot, ITenantScoped
{
    private ContractDocument()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public ContractDocumentKind Kind { get; private set; }

    public string Title { get; private set; } = null!;

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    /// <summary>The document's own reference (an agreement or annexure number). A new file with the same kind and number is the next version, never a replacement.</summary>
    public string? Number { get; private set; }

    public int DocumentVersion { get; private set; } = 1;

    public DateOnly? IssueDate { get; private set; }

    public DateOnly? EffectiveDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public DocumentStatus Status { get; private set; } = DocumentStatus.Pending;

    public Guid? VerifiedBy { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Records the descriptive details. Called by the upload before the document is saved.</summary>
    public Result Describe(string? number, int version, DateOnly? issue, DateOnly? effective, DateOnly? expiry)
    {
        if (number?.Trim().Length > 60 || version < 1 || (issue is { } i && effective is { } e && e < i) || (effective is { } from && expiry is { } to && to < from))
        {
            return Error.Validation("contract_documents.details_invalid", "The number can be 60 characters, and the effective date cannot be before the issue date or after the expiry.");
        }

        Number = string.IsNullOrWhiteSpace(number) ? null : number.Trim();
        DocumentVersion = version;
        IssueDate = issue;
        EffectiveDate = effective;
        ExpiryDate = expiry;
        return Result.Success();
    }

    public Result Verify(Guid? userId, DateTimeOffset now)
    {
        if (Status == DocumentStatus.Verified)
        {
            return Error.Conflict("contract_documents.already_verified", "This document has already been verified.");
        }

        Status = DocumentStatus.Verified;
        VerifiedBy = userId;
        VerifiedAt = now;
        return Result.Success();
    }

    public static Result<ContractDocument> Create(
        Guid tenantId, Guid contractId, ContractDocumentKind kind, string title, string fileKey, string fileName, string contentType, long sizeBytes)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            return Error.Validation("contract_documents.invalid", "A document type and a title of up to 200 characters are required.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["title"] = ["A title of up to 200 characters is required."] },
            };
        }

        return new ContractDocument
        {
            TenantId = tenantId,
            ContractId = contractId,
            Kind = kind,
            Title = title.Trim(),
            FileKey = fileKey,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
        };
    }
}

/// <summary>Remembers that the "expires in N days" reminder for a contract has been sent, so it is sent once.</summary>
[AuditIgnore]
public sealed class ExpiryAlert
{
    public Guid ContractId { get; init; }

    public int DaysBefore { get; init; }

    public Guid TenantId { get; init; }

    public DateTimeOffset SentAt { get; init; }
}
