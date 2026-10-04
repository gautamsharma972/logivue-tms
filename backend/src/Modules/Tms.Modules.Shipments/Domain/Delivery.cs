using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

/// <summary>Where a delivery's paperwork stands. It only moves once the goods have been delivered.</summary>
public enum PodStatus
{
    /// <summary>Delivered (or not yet), no proof uploaded.</summary>
    Awaiting = 1,

    /// <summary>Proof uploaded, waiting for staff to check it.</summary>
    Uploaded = 2,

    Verified = 3,

    /// <summary>Checked and refused; the transporter must upload a better copy.</summary>
    Rejected = 4,
}

/// <param name="DeliveredPackages">Packages received, including damaged ones. Null means all of them.</param>
/// <param name="DamagedPackages">Of those received, how many were damaged.</param>
public sealed record DeliveryDetails(DateTimeOffset DeliveredAt, string ReceiverName, int? DeliveredPackages, int? DamagedPackages, string? Remarks);

/// <summary>A scanned or photographed proof of delivery (signed LR copy, photo) attached to one order of a shipment.</summary>
public sealed class PodDocument : AggregateRoot, ITenantScoped
{
    public const int MaxPerOrder = 5;

    private PodDocument()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary>Whose shipment this is, so a vendor can be limited to its own documents.</summary>
    public Guid TransporterId { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public static PodDocument Create(
        Guid tenantId, Guid shipmentId, Guid orderId, Guid transporterId, string fileKey, string fileName, string contentType, long sizeBytes) =>
        new()
        {
            TenantId = tenantId, ShipmentId = shipmentId, OrderId = orderId, TransporterId = transporterId, FileKey = fileKey, FileName = fileName,
            ContentType = contentType, SizeBytes = sizeBytes,
        };
}

public static class DeliveryRules
{
    /// <summary>
    /// Checks what was received against what was shipped and returns the normalised figures. When the order has no package count,
    /// quantities are not tracked and must be left blank.
    /// </summary>
    public static Result<(int? Delivered, int? Damaged, int? Shortage)> Check(int? shipped, DeliveryDetails d)
    {
        if (string.IsNullOrWhiteSpace(d.ReceiverName) || d.ReceiverName.Trim().Length > 150)
        {
            return Fail("receiverName", "Enter who received the goods (up to 150 characters).");
        }

        if (d.Remarks is { } remarks && remarks.Trim().Length > 500)
        {
            return Fail("remarks", "Remarks can be at most 500 characters.");
        }

        if (shipped is null)
        {
            return d.DeliveredPackages is not null || d.DamagedPackages is not null
                ? Fail("deliveredPackages", "This order has no package count, so quantities cannot be recorded.")
                : (null, null, null);
        }

        var delivered = d.DeliveredPackages ?? shipped.Value;
        var damaged = d.DamagedPackages ?? 0;
        if (delivered < 0 || delivered > shipped)
        {
            return Fail("deliveredPackages", $"Delivered packages must be between 0 and {shipped}.");
        }

        if (damaged < 0 || damaged > delivered)
        {
            return Fail("damagedPackages", "Damaged packages cannot be more than the packages received.");
        }

        var shortage = shipped.Value - delivered;
        if ((shortage > 0 || damaged > 0) && string.IsNullOrWhiteSpace(d.Remarks))
        {
            return Fail("remarks", "Say what happened when goods arrive short or damaged.");
        }

        return (delivered, damaged, shortage);

        static Error Fail(string field, string message) => Error.Validation("delivery.invalid", message) with
        {
            ValidationErrors = new Dictionary<string, string[]> { [field] = [message] },
        };
    }
}
