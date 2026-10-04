using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Tms.BuildingBlocks.Web.Http;

/// <summary>Accepts or generates <c>X-Correlation-Id</c>, echoes it back and attaches it to every log line.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
