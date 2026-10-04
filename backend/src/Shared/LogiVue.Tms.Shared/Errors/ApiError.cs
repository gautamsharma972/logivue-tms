namespace LogiVue.Tms.Shared.Errors;

/// <summary>Standard error body returned by every API endpoint. Never contains stack traces.</summary>
public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyList<ApiErrorDetail> Details,
    string? TraceId);

/// <summary>A field-level or rule-level reason attached to an <see cref="ApiError"/>.</summary>
public sealed record ApiErrorDetail(string? Field, string Reason);
