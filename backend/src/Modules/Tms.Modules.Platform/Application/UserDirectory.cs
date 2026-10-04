using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Platform.Application;

/// <summary>Platform's implementation of the cross-module user lookup. Tenant-filtered like everything else.</summary>
internal sealed class UserDirectory(PlatformDbContext db) : IUserDirectory
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        return user?.EffectivePermissions ?? new HashSet<string>();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, UserContact>> GetContactsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive)
            .ToDictionaryAsync(u => u.Id, u => new UserContact(u.Id, u.FullName, u.Email), cancellationToken);
    }
}
