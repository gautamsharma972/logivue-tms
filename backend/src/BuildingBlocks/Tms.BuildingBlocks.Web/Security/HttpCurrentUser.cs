using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Tms.SharedKernel.Security;

namespace Tms.BuildingBlocks.Web.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId => ParseGuid(TmsClaimTypes.Subject);

    public Guid? TenantId => ParseGuid(TmsClaimTypes.Tenant);

    public Guid? TransporterId => ParseGuid(TmsClaimTypes.Transporter);

    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true;

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? TraceId => Activity.Current?.TraceId.ToString() ?? Context?.TraceIdentifier;

    public IReadOnlySet<string> Permissions =>
        Context?.User.FindAll(TmsClaimTypes.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
        ?? [];

    private Guid? ParseGuid(string claimType) =>
        Guid.TryParse(Context?.User.FindFirst(claimType)?.Value, out var id) ? id : null;
}
