using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Approvals.Application;

/// <summary>The document types that may be submitted for approval (collected from DI).</summary>
public sealed class DocumentTypeCatalog(IEnumerable<ApprovalDocumentType> types)
{
    private readonly IReadOnlyList<ApprovalDocumentType> _all = types.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    public IReadOnlyList<ApprovalDocumentType> All => _all;

    public ApprovalDocumentType? Find(string code) => _all.FirstOrDefault(t => t.Code == code);

    public string NameOf(string code) => Find(code)?.Name ?? code;
}
