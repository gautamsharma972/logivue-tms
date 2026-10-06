using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Endpoints;

/// <summary>
/// Pushes position and alert changes to whoever has the control tower open. A connection joins its own tenant's group (staff with tracking access) or its own transporter's group (a
/// vendor), so nobody hears about another tenant's or another carrier's vehicles. The push is a nudge with the new state: the page still reads from the API.
/// </summary>
[Authorize]
public sealed class TrackingHub(ICurrentUser user) : Hub
{
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:N}";

    public static string TransporterGroup(Guid tenantId, Guid transporterId) => $"transporter:{tenantId:N}:{transporterId:N}";

    public override async Task OnConnectedAsync()
    {
        if (user.TenantId is { } tenantId)
        {
            if (user.TransporterId is { } transporterId)
            {
                if (user.Permissions.Contains(TrackingPermissions.Execute))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, TransporterGroup(tenantId, transporterId));
                }
            }
            else if (user.Permissions.Contains(TrackingPermissions.Read) || user.Permissions.Contains(TrackingPermissions.Manage))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
            }
        }

        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRTrackingLiveNotifier(IHubContext<TrackingHub> hub) : ITrackingLiveNotifier
{
    public async Task PushAsync(Guid tenantId, string kind, object payload, Guid? transporterId, CancellationToken cancellationToken)
    {
        await hub.Clients.Group(TrackingHub.TenantGroup(tenantId)).SendAsync(kind, payload, cancellationToken);
        if (transporterId is { } t)
        {
            await hub.Clients.Group(TrackingHub.TransporterGroup(tenantId, t)).SendAsync(kind, payload, cancellationToken);
        }
    }
}
