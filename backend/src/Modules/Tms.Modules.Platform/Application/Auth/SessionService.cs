using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Auth;

/// <summary>Issues access + refresh tokens and manages refresh-token families.</summary>
internal sealed class SessionService(
    PlatformDbContext db,
    ITokenService tokens,
    ICurrentUser currentUser,
    IOptions<JwtOptions> jwt,
    TimeProvider clock)
{
    public async Task<AuthResult> IssueAsync(User user, Tenant tenant, Guid familyId, CancellationToken cancellationToken)
    {
        var permissions = user.EffectivePermissions.Order(StringComparer.Ordinal).ToArray();
        var access = tokens.CreateAccessToken(user, permissions);
        var (refreshToken, refreshHash) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(RefreshToken.Issue(
            user.TenantId,
            user.Id,
            familyId,
            refreshHash,
            clock.GetUtcNow().AddDays(jwt.Value.RefreshTokenDays),
            currentUser.IpAddress));

        await db.SaveChangesAsync(cancellationToken);
        return new AuthResult(new AuthResponse(access.Value, access.ExpiresAt, ToProfile(user, tenant)), refreshToken);
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        active.ForEach(t => t.Revoke(now));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Signs the user out everywhere, e.g. after their password changed.</summary>
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        active.ForEach(t => t.Revoke(now));
        await db.SaveChangesAsync(cancellationToken);
    }

    public static UserProfile ToProfile(User user, Tenant tenant) =>
        new(
            user.Id,
            user.Email,
            user.FullName,
            user.Type,
            user.TransporterId,
            tenant.Code,
            tenant.Name,
            user.Roles.Select(r => r.Name).Order(StringComparer.Ordinal).ToArray(),
            user.EffectivePermissions.Order(StringComparer.Ordinal).ToArray(),
            user.MustChangePassword);
}
