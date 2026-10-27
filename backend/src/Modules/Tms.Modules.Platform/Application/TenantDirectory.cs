using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Platform.Application;

internal sealed class TenantDirectory(PlatformDbContext db) : ITenantDirectory
{
    public Task<string?> GetCodeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (string?)t.Code).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<string>?> GetModulesAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken))?.Modules;
}
