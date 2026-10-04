using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Approvals.Application.Delegations;
using Tms.Modules.Approvals.Application.Policies;
using Tms.Modules.Approvals.Application.Requests;
using Tms.Modules.Approvals.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Approvals.Endpoints;

internal static class ApprovalEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("/api/v1/approvals").WithTags("Approvals");

        // Requests: any signed-in user. What they can see and do is decided by the handlers (permissions, delegation).
        var requests = root.MapGroup("/requests").RequireAuthorization();

        requests.MapGet("/", async ([AsParameters] ListRequestsQuery query, ListRequestsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListApprovalRequests")
            .Produces<PagedResult<RequestSummaryDto>>();

        requests.MapGet("/{id:guid}", async (Guid id, GetRequestHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetApprovalRequest")
            .Produces<RequestDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/{id:guid}/approve", async (Guid id, DecisionRequest body, DecideRequestHandler handler, CancellationToken ct) =>
                (await handler.ApproveAsync(id, body, ct)).ToHttpResult())
            .WithValidation<DecisionRequest>()
            .WithName("ApproveRequest")
            .Produces<RequestDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requests.MapPost("/{id:guid}/reject", async (Guid id, DecisionRequest body, DecideRequestHandler handler, CancellationToken ct) =>
                (await handler.RejectAsync(id, body, ct)).ToHttpResult())
            .WithValidation<DecisionRequest>()
            .WithName("RejectRequest")
            .Produces<RequestDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requests.MapPost("/{id:guid}/cancel", async (Guid id, CancelRequestHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("CancelRequest")
            .Produces<RequestDto>();

        // Policies: administrators only.
        var policies = root.MapGroup("/policies");

        policies.MapGet("/", async (ListPoliciesHandler handler, CancellationToken ct) => (await handler.HandleAsync(ct)).ToHttpResult())
            .RequirePermission(ApprovalPermissions.PoliciesManage)
            .WithName("ListApprovalPolicies")
            .Produces<IReadOnlyList<PolicyDto>>();

        policies.MapGet("/permissions", (ListStepPermissionsHandler handler) => handler.Handle().ToHttpResult())
            .RequirePermission(ApprovalPermissions.PoliciesManage)
            .WithName("ListStepPermissions")
            .Produces<IReadOnlyList<Tms.SharedKernel.Security.PermissionDefinition>>();

        policies.MapPut("/{documentType}", async (string documentType, SavePolicyRequest body, SavePolicyHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(documentType, body, ct)).ToHttpResult())
            .RequirePermission(ApprovalPermissions.PoliciesManage)
            .WithValidation<SavePolicyRequest>()
            .WithName("SaveApprovalPolicy")
            .Produces<PolicyDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Delegations: each user manages their own.
        var delegations = root.MapGroup("/delegations").RequireAuthorization();

        delegations.MapGet("/", async (ListDelegationsHandler handler, CancellationToken ct) => (await handler.HandleAsync(ct)).ToHttpResult())
            .WithName("ListDelegations")
            .Produces<DelegationsDto>();

        delegations.MapPost("/", async (CreateDelegationRequest body, CreateDelegationHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToCreatedResult(d => $"/api/v1/approvals/delegations/{d.Id}"))
            .WithValidation<CreateDelegationRequest>()
            .WithName("CreateDelegation")
            .Produces<DelegationDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        delegations.MapDelete("/{id:guid}", async (Guid id, RevokeDelegationHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("RevokeDelegation")
            .Produces(StatusCodes.Status204NoContent);
    }
}
