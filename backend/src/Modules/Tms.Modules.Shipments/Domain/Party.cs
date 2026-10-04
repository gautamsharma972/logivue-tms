using Tms.SharedKernel.India;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

/// <summary>A place goods are picked up from or delivered to, with a contact person on site.</summary>
public sealed record Party(string Name, string Line1, string City, string State, string Pincode, string? ContactName, string? ContactPhone)
{
    public string NormalCity => Text.Normalise(City);

    public string NormalState => Text.Normalise(State);

    public string Label => $"{City.Trim()}, {State.Trim()}";

    internal Result<Party> Validated(string field)
    {
        var errors = new Dictionary<string, string[]>();

        void Need(string key, string? value, string label, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[$"{field}.{key}"] = [$"{label} is required."];
            }
            else if (value.Trim().Length > max)
            {
                errors[$"{field}.{key}"] = [$"{label} must be at most {max} characters."];
            }
        }

        Need("name", Name, "Name", 200);
        Need("line1", Line1, "Address", 200);
        Need("city", City, "City", 100);
        Need("state", State, "State", 100);
        if (!IndianIdentifiers.IsValidPincode(Pincode))
        {
            errors[$"{field}.pincode"] = ["Enter a valid 6-digit pincode."];
        }

        if (!string.IsNullOrWhiteSpace(ContactPhone) && !IndianIdentifiers.IsValidMobile(ContactPhone))
        {
            errors[$"{field}.contactPhone"] = ["Enter a valid 10-digit mobile number."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        return new Party(Name.Trim(), Line1.Trim(), City.Trim(), State.Trim(), Pincode.Trim(),
            string.IsNullOrWhiteSpace(ContactName) ? null : ContactName.Trim(),
            string.IsNullOrWhiteSpace(ContactPhone) ? null : IndianIdentifiers.NormaliseMobile(ContactPhone));
    }
}

internal static class Text
{
    /// <summary>Upper-case, trimmed, inner whitespace collapsed: "  pune  " and "Pune" are the same city.</summary>
    public static string Normalise(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();
}
