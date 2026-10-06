using Tms.SharedKernel.Domain;

namespace Tms.Modules.Transporters.Domain;

/// <summary>
/// One thing Tracking saw on the road about a carrier's trip: a late pickup, a late delivery, a route deviation, a long stop, an unplanned stop, or tracking that went quiet.
/// Kept apart from the scorecard on purpose: a phone that stopped sending is not a carrier that performed badly, and nothing here moves the scorecard unless the business decides it should.
/// </summary>
public sealed class TrackingObservation : AggregateRoot, ITenantScoped
{
    private TrackingObservation()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>The id of the event that reported it, so an event delivered twice is one observation.</summary>
    public Guid SourceEventId { get; private set; }

    public Guid TransporterId { get; private set; }

    public string ShipmentReference { get; private set; } = null!;

    public string TripReference { get; private set; } = null!;

    /// <summary>PickupDelay, DeliveryDelay, RouteDeviation, ExcessDwell, UnplannedStop or TrackingCompliance.</summary>
    public string Kind { get; private set; } = null!;

    public decimal? Value { get; private set; }

    public string? Detail { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static TrackingObservation Create(Guid tenantId, Guid eventId, Guid transporterId, string shipment, string trip, string kind, decimal? value, string? detail, DateTimeOffset at) =>
        new() { TenantId = tenantId, SourceEventId = eventId, TransporterId = transporterId, ShipmentReference = shipment, TripReference = trip, Kind = kind, Value = value, Detail = detail, At = at };
}
