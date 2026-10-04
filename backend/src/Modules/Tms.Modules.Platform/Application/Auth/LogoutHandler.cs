using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Platform.Application.Auth;

internal sealed class LogoutHandler(PlatformDbContext db, ITokenService tokens, SessionService sessions)
{
    /// <summary>Idempotent: an unknown or already-revoked token is not an error.</summary>
    public async Task<Result> HandleAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Success();
        }

        var hash = tokens.HashRefreshToken(refreshToken);
        var familyId = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (familyId is { } id)
        {
            await sessions.RevokeFamilyAsync(id, cancellationToken);
        }

        return Result.Success();
    }
}
