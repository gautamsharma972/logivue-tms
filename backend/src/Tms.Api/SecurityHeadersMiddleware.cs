namespace Tms.Api;

/// <summary>Baseline response hardening for a JSON API.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cache-Control"] = "no-store";

        if (!environment.IsDevelopment())
        {
            // The API serves no HTML; the dev-only Scalar UI needs scripts, so CSP is production-only.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }

        return next(context);
    }
}
