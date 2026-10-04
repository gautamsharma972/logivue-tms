using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Platform.Application.Auth;

internal sealed class RefreshHandler(PlatformDbContext db, ITokenService tokens, SessionService sessions, TimeProvider clock)
{
    public async Task<Result<AuthResult>> HandleAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var now = clock.GetUtcNow();
        var hash = tokens.HashRefreshToken(refreshToken);

        var token = await db.RefreshTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (token.IsRevoked)
        {
            // A rotated token was presented again: assume it leaked and kill the whole session chain.
            await sessions.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            return AuthErrors.RefreshTokenReuse;
        }

        if (token.IsExpired(now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == token.TenantId, cancellationToken);

        if (user is null || tenant is null || !user.IsActive || !tenant.IsActive)
        {
            await sessions.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        token.Revoke(now);
        return await sessions.IssueAsync(user, tenant, token.FamilyId, cancellationToken);
    }
}
