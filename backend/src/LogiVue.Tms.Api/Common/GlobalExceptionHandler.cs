using System.Diagnostics;
using FluentValidation;
using LogiVue.Tms.Shared.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace LogiVue.Tms.Api.Common;

/// <summary>
/// Converts every unhandled exception into the standard <see cref="ApiError"/> body and logs it
/// with the request's trace id. Stack traces are logged, never returned to the caller.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        var (status, error) = exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                new ApiError("VALIDATION_ERROR", "One or more fields are invalid.", ToDetails(validation), traceId)),
            AppException app => (MapStatus(app), new ApiError(app.Code, app.Message, app.Details.ToList(), traceId)),
            _ => (
                StatusCodes.Status500InternalServerError,
                new ApiError("INTERNAL_ERROR", "The server could not complete the request.", [], traceId))
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path} trace {TraceId}",
                httpContext.Request.Method, httpContext.Request.Path, traceId);
        }
        else
        {
            logger.LogWarning("Request {Method} {Path} rejected with {StatusCode} {ErrorCode}: {Reason}",
                httpContext.Request.Method, httpContext.Request.Path, status, error.Code, error.Message);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(error, cancellationToken);
        return true;
    }

    private static int MapStatus(AppException exception) => exception switch
    {
        NotFoundException => StatusCodes.Status404NotFound,
        ConflictException => StatusCodes.Status409Conflict,
        ForbiddenException => StatusCodes.Status403Forbidden,
        BusinessRuleException => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError
    };

    private static IReadOnlyList<ApiErrorDetail> ToDetails(ValidationException exception) =>
        exception.Errors
            .Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage))
            .ToList();
}
