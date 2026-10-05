namespace Tms.Modules.Deliveries.Domain;

/// <summary>What the driver reports for one line of a delivery.</summary>
public sealed record ItemQuantities(
    Guid ItemId,
    decimal DeliveredQuantity,
    decimal ShortQuantity,
    decimal DamagedQuantity,
    decimal RejectedQuantity,
    string? ShortageReasonCode = null,
    string? DamageType = null,
    string? DamageReason = null,
    string? DamageDescription = null,
    string? Remarks = null);

/// <summary>How a line's reported quantities relate to what was dispatched. Never corrected silently: the difference is reported as it is.</summary>
/// <param name="Unaccounted">Dispatched minus everything accounted for. Positive: quantity not explained. Negative: more reported than was sent.</param>
public sealed record ReconciliationResult(Guid ItemId, string Sku, decimal Dispatched, decimal Accounted, decimal Unaccounted, bool Reconciled, IReadOnlyList<string> Problems);

public static class QuantityReconciliation
{
    public static decimal Accounted(ItemQuantities q) => q.DeliveredQuantity + q.ShortQuantity + q.DamagedQuantity + q.RejectedQuantity;

    /// <summary>Delivered + short + damaged + rejected must equal what was dispatched, and no quantity may be negative or exceed what the over-delivery allowance permits.</summary>
    public static ReconciliationResult Check(DeliveryItem item, ItemQuantities q, QuantityRulesSetting rules)
    {
        var problems = new List<string>();
        foreach (var (label, value) in new[] { ("Delivered", q.DeliveredQuantity), ("Short", q.ShortQuantity), ("Damaged", q.DamagedQuantity), ("Rejected", q.RejectedQuantity) })
        {
            if (value < 0)
            {
                problems.Add($"{label} quantity cannot be negative.");
            }
        }

        var accounted = Accounted(q);
        var allowed = item.DispatchedQuantity * (1 + rules.OverDeliveryPct / 100m);
        if (q.DeliveredQuantity > allowed)
        {
            problems.Add($"Delivered {q.DeliveredQuantity:0.##} is more than the {allowed:0.##} allowed for {item.SkuReference} (dispatched {item.DispatchedQuantity:0.##}).");
        }

        var unaccounted = item.DispatchedQuantity - accounted;
        var reconciled = problems.Count == 0 && (unaccounted == 0 || (unaccounted < 0 && -unaccounted <= item.DispatchedQuantity * rules.OverDeliveryPct / 100m));
        if (problems.Count == 0 && !reconciled)
        {
            problems.Add(unaccounted > 0
                ? $"{item.SkuReference}: {unaccounted:0.##} of {item.DispatchedQuantity:0.##} dispatched are not accounted for (delivered {q.DeliveredQuantity:0.##} + short {q.ShortQuantity:0.##} + damaged {q.DamagedQuantity:0.##} + rejected {q.RejectedQuantity:0.##})."
                : $"{item.SkuReference}: {-unaccounted:0.##} more than was dispatched is reported.");
        }

        return new ReconciliationResult(item.Id, item.SkuReference, item.DispatchedQuantity, accounted, unaccounted, reconciled, problems);
    }
}
