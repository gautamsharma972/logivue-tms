namespace Tms.SharedKernel.Results;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
}

/// <param name="Code">Stable, machine-readable code, e.g. <c>auth.invalid_credentials</c>.</param>
/// <param name="Description">Human-readable message safe to show to the caller.</param>
public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}
