using Tms.BuildingBlocks.Web.Http;
using Tms.SharedKernel.Contracts;

namespace Tms.Api;

/// <summary>
/// Conveniences that exist only until real document modules call <see cref="IApprovalGateway"/> themselves.
/// Mapped in Development and Testing only — never in production.
/// </summary>
internal static class DevEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/dev/approvals/submit", async (SubmitApproval request, IApprovalGateway gateway, CancellationToken ct) =>
                (await gateway.SubmitAsync(request, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithTags("Dev")
            .WithName("DevSubmitApproval");
}
