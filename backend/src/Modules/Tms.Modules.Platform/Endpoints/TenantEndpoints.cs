using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Application.Tenants;

namespace Tms.Modules.Platform.Endpoints;

/// <summary>Operator-only: which modules each customer organisation has. Guarded twice: the permission (platform administrators only) and the handler's own platform-organisation check.</summary>
internal static class TenantEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/platform").WithTags("Platform").RequireAuthorization().RequirePermission(TenantAccess.ManageTenants);

        g.MapGet("/modules", () => Results.Ok(TenantAdminHandler.Modules())).WithName("PlatformModules").Produces<IReadOnlyList<ModuleDto>>();
        g.MapGet("/tenants", async (TenantAdminHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult()).WithName("PlatformTenants").Produces<IReadOnlyList<TenantDto>>();
        g.MapPost("/tenants", async (CreateTenantRequest body, TenantAdminHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .WithValidation<CreateTenantRequest>().WithName("PlatformCreateTenant").Produces<ProvisionedTenant>();
        g.MapPost("/tenants/{id:guid}/reset-admin-password", async (Guid id, TenantAdminHandler h, CancellationToken ct) => (await h.ResetFirstAdminPasswordAsync(id, ct)).ToHttpResult())
            .WithName("PlatformResetAdminPassword").Produces<AdminPasswordResetDto>();
        g.MapPut("/tenants/{id:guid}", async (Guid id, UpdateTenantRequest body, TenantAdminHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<UpdateTenantRequest>().WithName("PlatformUpdateTenant").Produces<TenantDto>();
        g.MapPut("/tenants/{id:guid}/modules", async (Guid id, SetTenantModulesRequest body, TenantAdminHandler h, CancellationToken ct) => (await h.SetModulesAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SetTenantModulesRequest>().WithName("PlatformSetTenantModules").Produces<TenantDto>();
    }
}
