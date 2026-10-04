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
