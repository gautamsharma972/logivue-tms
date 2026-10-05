using System.Globalization;
using System.Text.RegularExpressions;

namespace Tms.Modules.Deliveries.Domain;

public sealed record OcrReading(string Name, string? Raw, string? Normalized, decimal Confidence);

public sealed record OcrCheck(string Name, OcrFieldStatus Status, string? Message);

/// <summary>
/// Compares what was read from the paper POD with what the system knows. A reading is only as good as its confidence and its agreement with the delivery:
/// either can send the proof to a person. Confidence thresholds are policy, not constants.
/// </summary>
public static partial class OcrReconciler
{
    public const string ShipmentNumber = "Shipment Number";
    public const string DeliveryNumber = "Delivery Number";
    public const string InvoiceNumber = "Invoice Number";
    public const string Customer = "Customer";
    public const string Transporter = "Transporter";
    public const string VehicleNumber = "Vehicle Number";
    public const string DeliveryDate = "Delivery Date";
    public const string RecipientName = "Recipient Name";
    public const string DeliveredQuantity = "Delivered Quantity";
    public const string ShortQuantity = "Short Quantity";
    public const string DamageRemarks = "Damage Remarks";

    public static IReadOnlyList<string> FieldNames { get; } =
        [ShipmentNumber, DeliveryNumber, InvoiceNumber, Customer, Transporter, VehicleNumber, DeliveryDate, RecipientName, DeliveredQuantity, ShortQuantity, DamageRemarks];

    public static decimal ThresholdFor(string field, OcrSetting s) =>
        s.CriticalFields.Contains(field, StringComparer.OrdinalIgnoreCase) ? s.CriticalThreshold
        : s.OptionalFields.Contains(field, StringComparer.OrdinalIgnoreCase) ? s.OptionalThreshold : s.StandardThreshold;

    public static IReadOnlyList<OcrCheck> Reconcile(IEnumerable<OcrReading> readings, Delivery delivery, PodRecord pod, OcrSetting settings)
    {
        var results = new List<OcrCheck>();
        foreach (var field in readings)
        {
            var value = field.Normalized ?? field.Raw;
            var (expected, comparer) = Expectation(field.Name, delivery, pod);
            OcrCheck check;
            if (field.Confidence < ThresholdFor(field.Name, settings))
            {
                check = new(field.Name, OcrFieldStatus.LowConfidence, $"Read with {field.Confidence:P0} confidence, below the {ThresholdFor(field.Name, settings):P0} needed.");
            }
            else if (expected is null || comparer is null || string.IsNullOrWhiteSpace(value))
            {
                check = new(field.Name, OcrFieldStatus.NotChecked, null);
            }
            else if (comparer(value, expected))
            {
                check = new(field.Name, OcrFieldStatus.Matched, null);
            }
            else
            {
                check = new(field.Name, OcrFieldStatus.Mismatch, $"The paper says '{field.Raw ?? value}'; the system has '{expected}'.");
            }

            results.Add(check);
        }

        return results;
    }

    private static (string? Expected, Func<string, string, bool>? Comparer) Expectation(string field, Delivery d, PodRecord pod) => field switch
    {
        ShipmentNumber => (d.ShipmentReference, SameCode),
        DeliveryNumber => (d.Number, SameCode),
        InvoiceNumber => (d.OrderReference, SameCode),
        Customer => (d.CustomerName, Contains),
        Transporter => (d.TransporterReference, Contains),
        VehicleNumber => (d.VehicleReference, SameCode),
        DeliveryDate => (d.ActualDeliveryAt is { } at ? at.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null, SameDate),
        RecipientName => (pod.RecipientName, Contains),
        DeliveredQuantity => (pod.Items.Sum(i => i.DeliveredQuantity).ToString("0.##", CultureInfo.InvariantCulture), SameNumber),
        ShortQuantity => (pod.Items.Sum(i => i.ShortQuantity).ToString("0.##", CultureInfo.InvariantCulture), SameNumber),
        _ => (null, null),
    };

    public static string Code(string value) => NonAlphanumeric().Replace(value, string.Empty).ToUpperInvariant();

    private static bool SameCode(string read, string expected) => Code(read) == Code(expected);

    private static bool Contains(string read, string expected)
    {
        var a = read.Trim().ToUpperInvariant();
        var b = expected.Trim().ToUpperInvariant();
        return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal) || b.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length > 2 && a.Contains(w, StringComparison.Ordinal));
    }

    private static bool SameNumber(string read, string expected) =>
        decimal.TryParse(read.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var a)
        && decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var b) && a == b;

    private static bool SameDate(string read, string expected) => TryDate(read, out var a) && TryDate(expected, out var b) && a == b;

    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "dd MMM yyyy", "d MMM yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
