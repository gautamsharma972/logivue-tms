using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Platform.Application;

internal sealed class TenantLimitsLookup(PlatformDbContext db) : ITenantLimits
{
    public async Task<TenantLimits> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new TenantLimits(t.MaxUsers, t.MaxLocations)).FirstOrDefaultAsync(cancellationToken) ?? new TenantLimits(null, null);
}
