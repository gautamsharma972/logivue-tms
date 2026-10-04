using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Platform.Application.Audit;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Platform.Endpoints;

internal static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/audit-logs", async ([AsParameters] ListAuditLogsQuery query, ListAuditLogsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(query, ct)).ToHttpResult())
            .RequirePermission(Permissions.AuditRead)
            .WithTags("Audit")
            .WithName("ListAuditLogs")
            .Produces<PagedResult<AuditLogDto>>();
}
