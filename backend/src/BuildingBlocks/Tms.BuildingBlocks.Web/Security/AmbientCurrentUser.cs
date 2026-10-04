using Tms.SharedKernel.Security;

namespace Tms.BuildingBlocks.Web.Security;

/// <summary>
/// The scoped <see cref="ICurrentUser"/>: reads the HTTP request when there is one, and can instead be told to act as
/// a specific tenant/user (background work).
/// </summary>
internal sealed class AmbientCurrentUser(HttpCurrentUser http) : ICurrentUser, IAmbientUserContext
{
    private bool _overridden;
    private Guid? _tenantId;
    private Guid? _userId;
    private string? _traceId;

    public void RunAs(Guid? tenantId, Guid? userId, string? traceId)
    {
        _overridden = true;
        (_tenantId, _userId, _traceId) = (tenantId, userId, traceId);
    }

    public Guid? UserId => _overridden ? _userId : http.UserId;

    public Guid? TenantId => _overridden ? _tenantId : http.TenantId;

    public Guid? TransporterId => _overridden ? null : http.TransporterId;

    public bool IsAuthenticated => !_overridden && http.IsAuthenticated;

    public string? IpAddress => _overridden ? null : http.IpAddress;

    public string? TraceId => _overridden ? _traceId : http.TraceId;

    /// <summary>Background work carries no permissions of its own; subscribers must not rely on caller permissions.</summary>
    public IReadOnlySet<string> Permissions => _overridden ? new HashSet<string>() : http.Permissions;
}
