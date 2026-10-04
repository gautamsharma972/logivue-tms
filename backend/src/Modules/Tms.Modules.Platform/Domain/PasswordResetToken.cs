using Tms.SharedKernel.Domain;

namespace Tms.Modules.Platform.Domain;

/// <summary>
/// A single-use link for choosing a new password (forgot-password, or the invitation for a new administrator).
/// Only a hash of the secret is stored, so a database leak does not yield working links.
/// </summary>
[AuditIgnore]
public sealed class PasswordResetToken : AggregateRoot, ITenantScoped
{
    private PasswordResetToken()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static PasswordResetToken Issue(Guid tenantId, Guid userId, string tokenHash, DateTimeOffset expiresAt) =>
        new() { TenantId = tenantId, UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt };

    public bool IsUsableAt(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
}
