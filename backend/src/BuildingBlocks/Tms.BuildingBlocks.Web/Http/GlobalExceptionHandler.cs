using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tms.SharedKernel.Domain;

namespace Tms.BuildingBlocks.Web.Http;

/// <summary>Last line of defence: converts unexpected exceptions into RFC 9457 problem responses without leaking internals.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was modified by someone else. Reload and try again.", "concurrency.conflict"),
            TenantViolationException => (StatusCodes.Status403Forbidden, "Forbidden", "tenant.violation"),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request", "request.invalid"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server.error"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Request failed with {Status} ({Code})", status, code);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code },
            },
        });
    }
}
