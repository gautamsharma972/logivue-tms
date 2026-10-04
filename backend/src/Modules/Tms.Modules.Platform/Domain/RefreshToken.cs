using Tms.SharedKernel.Domain;

namespace Tms.Modules.Platform.Domain;

/// <summary>
/// One link in a rotating refresh-token chain. Only a SHA-256 hash of the token is stored.
/// Presenting an already-rotated token revokes the whole <see cref="FamilyId"/> (theft detection).
/// </summary>
[AuditIgnore]
public sealed class RefreshToken : AggregateRoot, ITenantScoped
{
    private RefreshToken()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? CreatedByIp { get; private set; }

    public static RefreshToken Issue(Guid tenantId, Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, string? ip) =>
        new()
        {
            TenantId = tenantId,
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedByIp = ip,
        };

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
