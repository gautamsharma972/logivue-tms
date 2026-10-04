using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

/// <summary>Collects per-field problems so a form can show them all at once instead of one per submit.</summary>
internal sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public FieldErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
        return this;
    }

    public FieldErrors Require(string field, string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, $"{label} is required.");
        }
        else if (value.Trim().Length > maxLength)
        {
            Add(field, $"{label} must be at most {maxLength} characters.");
        }

        return this;
    }

    public Error ToError() =>
        Error.Validation("validation.failed", "One or more fields are invalid.") with
        {
            ValidationErrors = _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
        };
}
