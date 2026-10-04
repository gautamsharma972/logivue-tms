using System.Text.RegularExpressions;

namespace Tms.SharedKernel.India;

/// <summary>Validation and normalisation of identifiers used in Indian logistics and tax paperwork.</summary>
public static partial class IndianIdentifiers
{
    private const string GstinAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [GeneratedRegex("^[A-Z]{5}[0-9]{4}[A-Z]$")]
    private static partial Regex PanPattern();

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex GstinPattern();

    [GeneratedRegex("^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex IfscPattern();

    [GeneratedRegex("^[1-9][0-9]{5}$")]
    private static partial Regex PincodePattern();

    [GeneratedRegex("^[6-9][0-9]{9}$")]
    private static partial Regex MobilePattern();

    // State/UT series, optional district RTO number, optional series letters, 4-digit number: MH12AB1234, DL1CAB1234, KA01A1234.
    [GeneratedRegex("^[A-Z]{2}[0-9]{1,2}[A-Z]{0,3}[0-9]{4}$")]
    private static partial Regex VehiclePattern();

    public static string Normalise(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidPan(string? pan) => PanPattern().IsMatch(Normalise(pan));

    /// <summary>Checks format and the GSTIN check character (mod-36 checksum), so typos are caught, not just bad shapes.</summary>
    public static bool IsValidGstin(string? gstin)
    {
        var value = Normalise(gstin);
        return GstinPattern().IsMatch(value) && value[14] == GstinCheckCharacter(value);
    }

    /// <summary>The PAN embedded in a (valid) GSTIN, characters 3–12.</summary>
    public static string PanFromGstin(string gstin) => Normalise(gstin).Substring(2, 10);

    /// <summary>The two-digit GST state code (01–38), characters 1–2.</summary>
    public static string StateCodeFromGstin(string gstin) => Normalise(gstin)[..2];

    public static bool IsValidIfsc(string? ifsc) => IfscPattern().IsMatch(Normalise(ifsc));

    public static bool IsValidPincode(string? pincode) => PincodePattern().IsMatch((pincode ?? string.Empty).Trim());

    public static bool IsValidMobile(string? mobile) => MobilePattern().IsMatch(NormaliseMobile(mobile));

    /// <summary>Strips spaces, dashes and a leading +91 / 0.</summary>
    public static string NormaliseMobile(string? mobile)
    {
        var digits = new string((mobile ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.StartsWith("+91", StringComparison.Ordinal))
        {
            digits = digits[3..];
        }
        else if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        else if (digits.Length == 11 && digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        return digits;
    }

    public static string NormaliseVehicleRegistration(string? registration) =>
        new string(Normalise(registration).Where(char.IsLetterOrDigit).ToArray());

    public static bool IsValidVehicleRegistration(string? registration) =>
        VehiclePattern().IsMatch(NormaliseVehicleRegistration(registration));

    private static char GstinCheckCharacter(string gstin)
    {
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var value = GstinAlphabet.IndexOf(gstin[i], StringComparison.Ordinal) * (i % 2 == 0 ? 1 : 2);
            sum += (value / 36) + (value % 36);
        }

        return GstinAlphabet[(36 - (sum % 36)) % 36];
    }
}
