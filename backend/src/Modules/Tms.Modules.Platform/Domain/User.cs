using Tms.SharedKernel.Domain;

namespace Tms.Modules.Platform.Domain;

public enum UserType
{
    /// <summary>Employee of the shipper organisation.</summary>
    Internal = 1,

    /// <summary>User of a transporter (vendor) company, using the vendor portal.</summary>
    Transporter = 2,

    /// <summary>Driver using the mobile app.</summary>
    Driver = 3,
}

public sealed record UserCreated(Guid UserId, Guid TenantId) : DomainEvent;

public sealed class User : AggregateRoot, ITenantScoped
{
    public const int MaxFailedLogins = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<Role> _roles = [];

    private User()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>For <see cref="UserType.Transporter"/> users: the transporter company they belong to (owned by the Transporters module).</summary>
    public Guid? TransporterId { get; private set; }

    public string Email { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    [AuditIgnore]
    public string PasswordHash { get; private set; } = null!;

    public UserType Type { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Set when an administrator chose the password: the user must pick their own before doing anything else.</summary>
    public bool MustChangePassword { get; private set; }

    // Sign-in bookkeeping changes on every login; auditing it would bury real changes. Lockouts stay visible via LockedUntil.
    [AuditIgnore]
    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    [AuditIgnore]
    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<Role> Roles => _roles;

    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Create(Guid tenantId, string email, string fullName, string passwordHash, UserType type, IEnumerable<Role> roles, Guid? transporterId = null, bool mustChangePassword = false)
    {
        if ((type == UserType.Transporter) != (transporterId is not null))
        {
            throw new ArgumentException("Transporter users must be linked to a transporter, and only they may be.", nameof(transporterId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var user = new User
        {
            TenantId = tenantId,
            Email = NormaliseEmail(email),
            FullName = fullName.Trim(),
            PasswordHash = passwordHash,
            Type = type,
            TransporterId = transporterId,
            MustChangePassword = mustChangePassword,
            IsActive = true,
        };
        user._roles.AddRange(roles);
        user.Raise(new UserCreated(user.Id, tenantId));
        return user;
    }

    public IReadOnlySet<string> EffectivePermissions =>
        _roles.SelectMany(r => r.Permissions).ToHashSet(StringComparer.Ordinal);

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockedUntil = now + LockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }

    /// <summary>The user (or a reset link) chose a new password: clears the forced-change flag and any lockout.</summary>
    public void ChangePassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        MustChangePassword = false;
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    /// <summary>Transparent re-hash with stronger parameters; not a password change.</summary>
    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void UpdateProfile(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        FullName = fullName.Trim();
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetRoles(IEnumerable<Role> roles)
    {
        var target = roles.ToList();
        _roles.RemoveAll(r => target.All(t => t.Id != r.Id));
        _roles.AddRange(target.Where(t => _roles.All(r => r.Id != t.Id)));
    }
}
