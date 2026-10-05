using Tms.SharedKernel.Domain;

namespace Tms.Modules.Transporters.Domain;

/// <summary>
/// What the delivery module has reported about one delivery's proof, kept per transporter so its dependability can be read: on time, proof in time, accepted first
/// time, short or damaged, refused, failed. Fed only by events; nothing here changes the scorecard (its weights are the business's to decide).
/// </summary>
public sealed class ProofPerformance : AggregateRoot, ITenantScoped
{
    private ProofPerformance()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string DeliveryNumber { get; private set; } = null!;

    public DateTimeOffset? DeliveredAt { get; private set; }

    public bool? OnTime { get; private set; }

    public bool? SubmittedWithinSla { get; private set; }

    public bool? AcceptedFirstTime { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public int Rejections { get; private set; }

    public DateTimeOffset? LastRejectedAt { get; private set; }

    public decimal ShortQuantity { get; private set; }

    public decimal DamagedQuantity { get; private set; }

    public bool Refused { get; private set; }

    public bool Failed { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProofPerformance Open(Guid tenantId, Guid deliveryId, Guid transporterId, string number, DateTimeOffset now) =>
        new() { TenantId = tenantId, DeliveryId = deliveryId, TransporterId = transporterId, DeliveryNumber = number, UpdatedAt = now };

    public void Delivered(DateTimeOffset at, bool? onTime, decimal shortQuantity, decimal damagedQuantity, DateTimeOffset now)
    {
        DeliveredAt = at;
        OnTime = onTime;
        ShortQuantity = shortQuantity;
        DamagedQuantity = damagedQuantity;
        UpdatedAt = now;
    }

    public void Accepted(DateTimeOffset at, bool firstTime, bool? withinSla, bool? onTime, decimal shortQuantity, decimal damagedQuantity, DateTimeOffset delivered, DateTimeOffset now)
    {
        AcceptedAt = at;
        AcceptedFirstTime = firstTime;
        SubmittedWithinSla = withinSla;
        OnTime ??= onTime;
        ShortQuantity = shortQuantity;
        DamagedQuantity = damagedQuantity;
        DeliveredAt ??= delivered;
        UpdatedAt = now;
    }

    /// <summary>Counts a rejection once: an event delivered twice carries the same time.</summary>
    public void Rejected(DateTimeOffset at, DateTimeOffset now)
    {
        if (LastRejectedAt is { } last && at <= last)
        {
            return;
        }

        Rejections++;
        LastRejectedAt = at;
        UpdatedAt = now;
    }

    public void MarkRefused(DateTimeOffset now)
    {
        Refused = true;
        UpdatedAt = now;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Failed = true;
        UpdatedAt = now;
    }
}
