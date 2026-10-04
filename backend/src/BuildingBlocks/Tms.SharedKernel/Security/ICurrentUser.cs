namespace Tms.SharedKernel.Security;

/// <summary>The caller on whose behalf the current operation runs. Null ids mean "system / anonymous".</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    /// <summary>Set for vendor-portal users: every transporter-owned record they touch is limited to this transporter.</summary>
    Guid? TransporterId { get; }

    bool IsAuthenticated { get; }

    string? IpAddress { get; }

    string? TraceId { get; }

    /// <summary>Permission codes granted to the caller by their roles (from the access token).</summary>
    IReadOnlySet<string> Permissions { get; }
}

/// <summary>Used by design-time tooling, seeders and background work that runs outside a request.</summary>
public sealed class SystemUser : ICurrentUser
{
    public static readonly SystemUser Instance = new();

    public Guid? UserId => null;

    public Guid? TenantId => null;

    public Guid? TransporterId => null;

    public bool IsAuthenticated => false;

    public string? IpAddress => null;

    public string? TraceId => null;

    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
}

/// <summary>
/// Lets background work (outbox delivery, scheduled jobs) run "as" the tenant and user that caused it, so tenant
/// filtering and audit attribution behave exactly as they would in the original request.
/// </summary>
public interface IAmbientUserContext
{
    void RunAs(Guid? tenantId, Guid? userId, string? traceId);
}
