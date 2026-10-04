using Microsoft.AspNetCore.Http;

namespace Tms.BuildingBlocks.Web.Http;

/// <summary>
/// CSRF defence for cookie-authenticated endpoints (refresh, logout): browsers cannot attach a custom header to a
/// cross-site form post or image request, and a cross-origin fetch carrying one needs a CORS preflight we do not grant.
/// </summary>
public sealed class ClientHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-TMS-Client";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers.ContainsKey(HeaderName)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing client header",
                extensions: new Dictionary<string, object?> { ["code"] = "request.csrf_header_missing" }));
}
