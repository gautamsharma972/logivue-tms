namespace LogiVue.Tms.Shared.Errors;

/// <summary>
/// Base type for expected, business-level failures. The global exception handler maps
/// each subtype to an HTTP status and a stable <see cref="Code"/>.
/// </summary>
public abstract class AppException(string code, string message, IReadOnlyList<ApiErrorDetail>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyList<ApiErrorDetail> Details { get; } = details ?? [];
}

/// <summary>The requested resource does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string message, string code = "NOT_FOUND")
    : AppException(code, message);

/// <summary>The request conflicts with existing state, e.g. a duplicate code (HTTP 409).</summary>
public sealed class ConflictException(string message, string code = "CONFLICT")
    : AppException(code, message);

/// <summary>The request is well-formed but violates a business rule (HTTP 422).</summary>
public sealed class BusinessRuleException(string message, string code = "BUSINESS_RULE_VIOLATION", IReadOnlyList<ApiErrorDetail>? details = null)
    : AppException(code, message, details);

/// <summary>The caller is authenticated but not permitted to perform the action (HTTP 403).</summary>
public sealed class ForbiddenException(string message, string code = "FORBIDDEN")
    : AppException(code, message);
