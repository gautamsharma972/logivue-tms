using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Platform.Application.Roles;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Endpoints;

internal static class RoleEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var roles = app.MapGroup("/api/v1/roles").WithTags("Roles");

        roles.MapGet("/", async (ListRolesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult())
            .RequirePermission(Permissions.RolesRead)
            .WithName("ListRoles")
            .Produces<IReadOnlyList<RoleDto>>();

        roles.MapPost("/", async (SaveRoleRequest request, CreateRoleHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToCreatedResult(r => $"/api/v1/roles/{r.Id}"))
            .RequirePermission(Permissions.RolesManage)
            .WithValidation<SaveRoleRequest>()
            .WithName("CreateRole")
            .Produces<RoleDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapPut("/{id:guid}", async (Guid id, SaveRoleRequest request, UpdateRoleHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.RolesManage)
            .WithValidation<SaveRoleRequest>()
            .WithName("UpdateRole")
            .Produces<RoleDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/api/v1/permissions", (ListPermissionsHandler handler) => handler.Handle().ToHttpResult())
            .RequirePermission(Permissions.RolesRead)
            .WithTags("Roles")
            .WithName("ListPermissions")
            .Produces<IReadOnlyList<PermissionDefinition>>();
    }
}
