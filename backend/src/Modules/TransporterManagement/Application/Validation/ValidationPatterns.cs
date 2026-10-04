using System.Text.RegularExpressions;

namespace LogiVue.Tms.TransporterManagement.Application.Validation;

/// <summary>Format rules shared by validators. Values are normalised to upper case before matching.</summary>
internal static partial class ValidationPatterns
{
    public static bool IsTransporterCode(string value) => TransporterCodeRegex().IsMatch(value);

    public static bool IsPan(string value) => PanRegex().IsMatch(value.ToUpperInvariant());

    public static bool IsGstin(string value) => GstinRegex().IsMatch(value.ToUpperInvariant());

    public static bool IsPhone(string value) => PhoneRegex().IsMatch(value);

    public static bool IsRegistrationNumber(string value) => RegistrationRegex().IsMatch(value);

    public static bool IsServiceType(string value) => ServiceTypeRegex().IsMatch(value);

    public static bool IsBranchCode(string value) => BranchCodeRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9-]{3,30}$")]
    private static partial Regex TransporterCodeRegex();

    [GeneratedRegex("^[A-Z]{5}[0-9]{4}[A-Z]$")]
    private static partial Regex PanRegex();

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex GstinRegex();

    [GeneratedRegex(@"^\+?[0-9][0-9 ()\-]{5,29}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex("^[A-Za-z0-9 -]{4,20}$")]
    private static partial Regex RegistrationRegex();

    [GeneratedRegex("^[A-Za-z_]{2,30}$")]
    private static partial Regex ServiceTypeRegex();

    [GeneratedRegex("^[A-Za-z0-9-]{2,30}$")]
    private static partial Regex BranchCodeRegex();
}
