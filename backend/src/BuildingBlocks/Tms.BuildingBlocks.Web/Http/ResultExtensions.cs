using Microsoft.AspNetCore.Http;
using Tms.SharedKernel.Results;

namespace Tms.BuildingBlocks.Web.Http;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();

    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? Results.Created(location(result.Value), result.Value) : result.Error.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error is { Type: ErrorType.Validation, ValidationErrors: { } validation })
        {
            return Results.ValidationProblem(
                validation.ToDictionary(kv => kv.Key, kv => kv.Value),
                title: "One or more validation errors occurred.",
                extensions: extensions);
        }

        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Bad request"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            _ => (StatusCodes.Status500InternalServerError, "Server error"),
        };

        return Results.Problem(statusCode: status, title: title, detail: error.Description, extensions: extensions);
    }
}
