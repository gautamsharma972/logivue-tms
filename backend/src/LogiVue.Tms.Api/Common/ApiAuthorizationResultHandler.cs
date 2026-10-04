using System.Diagnostics;
using LogiVue.Tms.Shared.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace LogiVue.Tms.Api.Common;

/// <summary>Writes authentication and role failures in the standard <see cref="ApiError"/> shape.</summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("UNAUTHENTICATED", "Sign in is required.", [], traceId));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ApiError("FORBIDDEN", "Your role does not allow this action.", [], traceId));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
