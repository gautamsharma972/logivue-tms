using System.Security.Claims;
using LogiVue.Tms.Shared.Authorization;

namespace LogiVue.Tms.Api.Common;

/// <summary>
/// Reads the caller from the authenticated principal. Claims: <c>sub</c> (user id), <c>name</c>, <c>roles</c>
/// and <c>transporter_id</c> (set only for transporter users, which scopes them to their own data).
/// Falls back to "system" when there is no request, such as background work.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string UserId => Principal?.FindFirstValue("sub") ?? "system";

    public string DisplayName => Principal?.FindFirstValue("name") ?? UserId;

    public long? TransporterId =>
        long.TryParse(Principal?.FindFirstValue("transporter_id"), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll("roles").Select(c => c.Value).ToList() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
