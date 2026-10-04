using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tms.BuildingBlocks.Web.Http;

/// <summary>Validates the bound <typeparamref name="TRequest"/> argument and short-circuits with a 400 problem.</summary>
public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is not null)
        {
            var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

                return Results.ValidationProblem(
                    errors,
                    title: "One or more validation errors occurred.",
                    extensions: new Dictionary<string, object?> { ["code"] = "validation.failed" });
            }
        }

        return await next(context);
    }

    private static string ToCamelCase(string path) =>
        string.Join('.', path.Split('.').Select(s => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..]));
}

public static class ValidationEndpointExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder.AddEndpointFilter<ValidationEndpointFilter<TRequest>>();
}
