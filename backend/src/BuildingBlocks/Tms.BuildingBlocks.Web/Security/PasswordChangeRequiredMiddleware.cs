using Microsoft.AspNetCore.Http;

namespace Tms.BuildingBlocks.Web.Security;

/// <summary>
/// While a user holds a temporary / admin-set password their token carries <c>mcp</c>; until they choose their own
/// password the API refuses everything except what they need to do that.
/// </summary>
public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    private static readonly string[] Allowed =
    [
        "/api/v1/auth/change-password",
        "/api/v1/auth/me",
        "/api/v1/auth/logout",
        "/api/v1/auth/refresh",
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(c => c.Type == TmsClaimTypes.MustChangePassword)
            && context.Request.Path.StartsWithSegments("/api")
            && !Allowed.Contains(context.Request.Path.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Password change required",
                detail: "Choose a new password before continuing.",
                extensions: new Dictionary<string, object?> { ["code"] = "auth.password_change_required" }).ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
