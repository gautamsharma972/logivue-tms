using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Auth;

internal sealed class GetCurrentUserHandler(PlatformDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<UserProfile>> HandleAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Error.Unauthorized("auth.user_not_found", "The signed-in user no longer exists or is inactive.");
        }

        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == user.TenantId, cancellationToken);
        return SessionService.ToProfile(user, tenant);
    }
}
