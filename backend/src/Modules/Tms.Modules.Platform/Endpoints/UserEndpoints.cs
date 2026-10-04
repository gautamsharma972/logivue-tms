using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Platform.Endpoints;

internal static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users");

        group.MapGet("/", async ([AsParameters] ListUsersQuery query, ListUsersHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(query, ct)).ToHttpResult())
            .RequirePermission(Permissions.UsersRead)
            .WithName("ListUsers")
            .Produces<PagedResult<UserDto>>();

        group.MapGet("/lookup", async (string? search, UserLookupHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(search, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("LookupUsers")
            .Produces<IReadOnlyList<UserLookupDto>>();

        group.MapGet("/{id:guid}", async (Guid id, GetUserHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.UsersRead)
            .WithName("GetUser")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateUserRequest request, CreateUserHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToCreatedResult(u => $"/api/v1/users/{u.Id}"))
            .RequirePermission(Permissions.UsersManage)
            .WithValidation<CreateUserRequest>()
            .WithName("CreateUser")
            .Produces<UserDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, UpdateUserHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.UsersManage)
            .WithValidation<UpdateUserRequest>()
            .WithName("UpdateUser")
            .Produces<UserDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
