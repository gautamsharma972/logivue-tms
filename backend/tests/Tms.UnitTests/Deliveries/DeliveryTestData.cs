using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Results;

namespace Tms.UnitTests.Deliveries;

internal static class DeliveryTestData
{
    public static readonly Guid Tenant = Guid.NewGuid();
    public static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    public static readonly Actor Driver = new(Guid.NewGuid(), "device-1");
    public static readonly QuantityRulesSetting Strict = new(0, false);
    public static readonly GeoFix Here = new(18.5204, 73.8567, 12);

    public static Delivery.Header Header(Guid? transporter = null, double? lat = null, double? lon = null, int? radius = null, DateTimeOffset? windowEnd = null) => new(
        Guid.NewGuid(), "SH-00010", Guid.NewGuid(), "INV9001", null, null, "LR-000001", 1, transporter ?? Guid.NewGuid(), "Shree Roadlines", Guid.NewGuid(), "MH12AB1234", "Ramesh",
        "CUST-1", "ABC Distributors", "9876543210", "abc@example.com", "Pune", "Surat", "Plot 1, Surat", lat, lon, radius, Now.AddHours(2), Now, windowEnd ?? Now.AddHours(4));

    public static Delivery New(decimal dispatched = 100, Guid? transporter = null, double? lat = null, double? lon = null, int? radius = null, params (string Sku, decimal Qty)[] more)
    {
        var items = new List<Delivery.ItemInput> { new("SKU-001", "Widgets", dispatched, dispatched, "PKG") };
        items.AddRange(more.Select(m => new Delivery.ItemInput(m.Sku, m.Sku, m.Qty, m.Qty, "PKG")));
        return Delivery.Create(Tenant, "DLV-00001", Header(transporter, lat, lon, radius), items, Driver, Now).Value;
    }

    public static Delivery Arrived(decimal dispatched = 100, params (string Sku, decimal Qty)[] more)
    {
        var d = New(dispatched, null, null, null, null, more);
        d.Start(Here, Driver, Now).IsSuccess.ShouldBeTrue();
        d.Arrive(Here, Driver, Now.AddMinutes(30)).IsSuccess.ShouldBeTrue();
        return d;
    }

    public static ItemQuantities Qty(Delivery d, decimal delivered, decimal @short = 0, decimal damaged = 0, decimal rejected = 0, int index = 0) =>
        new(d.Items[index].Id, delivered, @short, damaged, rejected, @short > 0 ? "SHORT_LOADED" : null, damaged > 0 ? "BROKEN" : null, damaged > 0 ? "Crushed in transit" : null);

    public static Result<CompletionResult> Complete(Delivery d, DeliveryOutcome outcome, params ItemQuantities[] q) =>
        d.Complete(outcome, q, null, Now.AddMinutes(45), null, "Ramesh", Here, Strict, Driver, Now.AddMinutes(45));

    public static PodRecord PodFor(Delivery d, ProofMethod method = ProofMethod.Photo, string? recipient = "Anil Kumar")
    {
        var pod = PodRecord.Create(d, "POD-00001", Here, GeofenceStatus.NotApplicable, null, Now);
        pod.SetProof(method, recipient, "Store manager", "9876543210", null, false);
        return pod;
    }

    public static PodEvidence AddPhoto(PodRecord pod, string hash = "h1", EvidenceType type = EvidenceType.PackagePhoto) =>
        pod.AddEvidence(type, $"k/{hash}.jpg", $"{hash}.jpg", "image/jpeg", 1000, hash, Now, Here, "device-1", null, 800, 600, null, null).Value;

    public static readonly PodRulesSetting Rules = (PodRulesSetting)DeliverySettingDefaults.For(DeliverySettingKeys.PodRules)!;
    public static readonly DiscrepancyRulesSetting Discrepancy = (DiscrepancyRulesSetting)DeliverySettingDefaults.For(DeliverySettingKeys.Discrepancy)!;
    public static readonly IReadOnlyCollection<string> PhotoDamage = ["BROKEN", "CRUSHED"];
}
