using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;

namespace Tms.Modules.Transporters.Application.MasterData;

/// <summary>The tenant's document rules and master lists, read once per request.</summary>
internal sealed class DocumentPolicyProvider(TransportersDbContext db)
{
    private DocumentPolicy? _policy;
    private List<MasterItem>? _items;

    public async Task<DocumentPolicy> GetAsync(CancellationToken cancellationToken) =>
        _policy ??= new DocumentPolicy(await db.DocumentRules.AsNoTracking().ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<MasterItem>> ItemsAsync(CancellationToken cancellationToken) =>
        _items ??= await db.MasterItems.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<bool> IsActiveAsync(MasterKind kind, string? code, CancellationToken cancellationToken) =>
        MasterCatalog.IsActive(kind, await ItemsAsync(cancellationToken), code);
}
