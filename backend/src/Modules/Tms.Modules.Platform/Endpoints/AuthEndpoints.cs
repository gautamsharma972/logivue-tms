using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Platform.Application.Auth;
using Tms.Modules.Platform.Infrastructure.Security;

namespace Tms.Modules.Platform.Endpoints;

internal static class AuthEndpoints
{
    internal const string RefreshCookie = "tms_refresh";

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, LoginHandler handler, HttpContext http, IOptions<JwtOptions> jwt, CancellationToken ct) =>
                SessionResult(http, jwt.Value, await handler.HandleAsync(request, ct)))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithValidation<LoginRequest>()
            .WithName("Login")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Cookie-authenticated: protected against cross-site requests by the required client header.
        group.MapPost("/refresh", async (RefreshHandler handler, HttpContext http, IOptions<JwtOptions> jwt, CancellationToken ct) =>
                SessionResult(http, jwt.Value, await handler.HandleAsync(http.Request.Cookies[RefreshCookie], ct)))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<ClientHeaderFilter>()
            .WithName("RefreshToken")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (LogoutHandler handler, HttpContext http, CancellationToken ct) =>
            {
                await handler.HandleAsync(http.Request.Cookies[RefreshCookie], ct);
                http.Response.Cookies.Delete(RefreshCookie, CookieOptions(http, 0));
                return Results.NoContent();
            })
            .AllowAnonymous()
            .AddEndpointFilter<ClientHeaderFilter>()
            .WithName("Logout")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (GetCurrentUserHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .Produces<UserProfile>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/change-password", async (ChangePasswordRequest request, ChangePasswordHandler handler, HttpContext http, IOptions<JwtOptions> jwt, CancellationToken ct) =>
                SessionResult(http, jwt.Value, await handler.HandleAsync(request, ct)))
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithValidation<ChangePasswordRequest>()
            .WithName("ChangePassword")
            .Produces<AuthResponse>()
            .ProducesValidationProblem();

        // Both answer identically whether or not the account exists.
        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, ForgotPasswordHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(request, ct);
                return Results.Accepted();
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithValidation<ForgotPasswordRequest>()
            .WithName("ForgotPassword")
            .Produces(StatusCodes.Status202Accepted);

        group.MapPost("/reset-password", async (ResetPasswordRequest request, ResetPasswordHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToHttpResult())
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithValidation<ResetPasswordRequest>()
            .WithName("ResetPassword")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
    }

    private static IResult SessionResult(HttpContext http, JwtOptions jwt, Tms.SharedKernel.Results.Result<AuthResult> result)
    {
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        http.Response.Cookies.Append(RefreshCookie, result.Value.RefreshToken, CookieOptions(http, jwt.RefreshTokenDays));
        return Results.Ok(result.Value.Response);
    }

    /// <summary>HttpOnly (script cannot read it), SameSite=Strict, scoped to the auth routes, Secure whenever the request was HTTPS.</summary>
    private static CookieOptions CookieOptions(HttpContext http, int days) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        MaxAge = days > 0 ? TimeSpan.FromDays(days) : null,
    };
}
