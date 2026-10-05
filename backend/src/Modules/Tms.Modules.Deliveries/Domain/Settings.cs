namespace Tms.Modules.Deliveries.Domain;

/// <summary>Business policy for deliveries and proofs. Nothing here is hard-coded into the rules: a tenant's own value (a row) wins over these defaults.</summary>
public static class DeliverySettingKeys
{
    public const string AttemptReasons = "delivery.attemptReasons";
    public const string ShortageReasons = "delivery.shortageReasons";
    public const string DamageTypes = "delivery.damageTypes";
    public const string RefusalReasons = "delivery.refusalReasons";
    public const string PodRules = "pod.rules";
    public const string Quantity = "delivery.quantity";
    public const string Discrepancy = "delivery.discrepancy";
    public const string Ocr = "pod.ocr";
    public const string AutoAccept = "pod.autoAccept";
    public const string Exceptions = "delivery.exceptions";
    public const string Images = "pod.images";
    public const string Sla = "pod.sla";
}

public sealed record ReasonSetting(string Code, string Name, bool EvidenceRequired = false);

/// <param name="MaxGpsAccuracyM">A fix vaguer than this cannot show where the vehicle was, so geofence checks report it instead of guessing.</param>
public sealed record PodRulesSetting(
    bool SignatureRequired, bool OtpRequired, bool GpsRequired, bool PhotoRequired, int MinPhotos, bool GeofenceRequired, bool ContactlessAllowed, bool GalleryAllowed,
    double MaxGpsAccuracyM, int OtpValidityMinutes, int OtpMaxAttempts);

/// <param name="OverDeliveryPct">How far above the dispatched quantity a delivered quantity may go (0 = never).</param>
/// <param name="BlockUnreconciledCompletion">When true a delivery whose quantities do not add up cannot be completed; otherwise it completes and raises a quantity-mismatch exception.</param>
public sealed record QuantityRulesSetting(decimal OverDeliveryPct, bool BlockUnreconciledCompletion);

public sealed record DiscrepancyRulesSetting(bool ShortageAcknowledgementRequired, bool DamageAcknowledgementRequired, bool RefusalAcknowledgementRequired, bool AutoCreateClaim);

/// <param name="CriticalFields">Fields the thresholds treat strictly (they identify the delivery).</param>
/// <param name="ReviewBelow">An overall confidence below this always goes to a human, whatever the individual fields say.</param>
public sealed record OcrSetting(
    bool Enabled, decimal CriticalThreshold, decimal StandardThreshold, decimal OptionalThreshold, decimal ReviewBelow,
    IReadOnlyList<string> CriticalFields, IReadOnlyList<string> OptionalFields);

/// <param name="Enabled">When off every submitted proof goes to a reviewer.</param>
/// <param name="RequireOcr">Accept automatically only if the paper POD was read and matched.</param>
/// <param name="AllowWithDiscrepancy">Shortage / damage cases may be accepted without a reviewer. Off unless a tenant explicitly turns it on.</param>
public sealed record AutoAcceptSetting(bool Enabled, bool RequireOcr, bool AllowWithDiscrepancy);

public sealed record ExceptionRulesSetting(int DueHours, int EscalateAfterHours, IReadOnlyDictionary<string, string> Severity);

public sealed record ImageRulesSetting(int MaxBytes, int MinWidth, int MinHeight, bool RejectLowResolution);

public sealed record SlaSetting(int PodSubmissionHours, int PodReviewHours, int ResubmissionHours);

public static class DeliverySettingDefaults
{
    private static readonly Dictionary<string, object> Values = new()
    {
        [DeliverySettingKeys.AttemptReasons] = new List<ReasonSetting>
        {
            new("CUSTOMER_UNAVAILABLE", "Customer unavailable"), new("ADDRESS_INCORRECT", "Address incorrect"), new("CUSTOMER_REFUSED", "Customer refused"),
            new("VEHICLE_ACCESS", "Vehicle unable to access"), new("RESCHEDULE_REQUESTED", "Customer requested reschedule"), new("DOCUMENTATION_ISSUE", "Documentation issue"),
            new("DAMAGED_SHIPMENT", "Damaged shipment"), new("PAYMENT_ISSUE", "Payment issue"), new("SITE_CLOSED", "Warehouse / customer closure"),
            new("WEATHER", "Weather"), new("TRAFFIC", "Traffic"), new("OTHER", "Other"),
        },
        [DeliverySettingKeys.ShortageReasons] = new List<ReasonSetting>
        {
            new("SHORT_LOADED", "Short loaded"), new("SHORT_DISPATCHED", "Short dispatched"), new("MISSING_IN_TRANSIT", "Missing in transit"),
            new("PARTIAL_ACCEPTANCE", "Customer partial acceptance"), new("PACKAGE_MISSING", "Package missing"), new("UNKNOWN", "Unknown"),
        },
        [DeliverySettingKeys.DamageTypes] = new List<ReasonSetting>
        {
            new("BROKEN", "Broken", true), new("CRUSHED", "Crushed", true), new("LEAKING", "Leaking", true), new("WET", "Wet", true), new("TORN", "Torn", true),
            new("TAMPERED", "Tampered", true), new("PACKAGING_DAMAGE", "Packaging damage"), new("PRODUCT_DAMAGE", "Product damage", true), new("OTHER", "Other"),
        },
        [DeliverySettingKeys.RefusalReasons] = new List<ReasonSetting>
        {
            new("DAMAGED_GOODS", "Damaged goods"), new("WRONG_ITEM", "Wrong item"), new("WRONG_QUANTITY", "Wrong quantity"), new("LATE_DELIVERY", "Late delivery"),
            new("COMMERCIAL_DISPUTE", "Price / commercial dispute"), new("CUSTOMER_CLOSED", "Customer closed"), new("CUSTOMER_NOT_AVAILABLE", "Customer not available"),
            new("QUALITY_ISSUE", "Quality issue"), new("OTHER", "Other"),
        },
        [DeliverySettingKeys.PodRules] = new PodRulesSetting(false, false, true, true, 1, false, true, false, 100, 30, 5),
        [DeliverySettingKeys.Quantity] = new QuantityRulesSetting(0, false),
        [DeliverySettingKeys.Discrepancy] = new DiscrepancyRulesSetting(false, false, false, false),
        [DeliverySettingKeys.Ocr] = new OcrSetting(true, 0.95m, 0.85m, 0.70m, 0.60m,
            ["Shipment Number", "Delivery Number", "Invoice Number", "Vehicle Number"], ["Damage Remarks"]),
        [DeliverySettingKeys.AutoAccept] = new AutoAcceptSetting(true, false, false),
        [DeliverySettingKeys.Exceptions] = new ExceptionRulesSetting(24, 48, new Dictionary<string, string>
        {
            ["Shortage"] = "Medium", ["Damage"] = "High", ["CustomerRefusal"] = "High", ["DeliveryFailed"] = "High", ["LateDelivery"] = "Low", ["AddressIssue"] = "Medium",
            ["PodMissing"] = "Medium", ["PodRejected"] = "Medium", ["QuantityMismatch"] = "High", ["GpsException"] = "Low", ["SignatureMissing"] = "Medium",
            ["OcrValidationFailed"] = "Low", ["DuplicatePod"] = "High", ["PartialDelivery"] = "Medium",
        }),
        [DeliverySettingKeys.Images] = new ImageRulesSetting(10 * 1024 * 1024, 320, 240, false),
        [DeliverySettingKeys.Sla] = new SlaSetting(24, 4, 12),
    };

    public static IReadOnlyCollection<string> Keys => Values.Keys;

    public static object? For(string key) => Values.GetValueOrDefault(key);

    public static Type? TypeOf(string key) => Values.GetValueOrDefault(key)?.GetType();
}
