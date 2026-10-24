using Tms.SharedKernel.Domain;

namespace Tms.Modules.TransportOps.Domain;

/// <summary>
/// One thing that happened on a trip or LR, kept for good. The actual time is when it happened; the recorded time is when the server learned of it. A correction is a new event that
/// points at the one it corrects, so the history shows both.
/// </summary>
[AuditIgnore]
public sealed class DeliveryEvent : Entity, ITenantScoped
{
    private DeliveryEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid? TripId { get; private set; }

    public Guid? LrId { get; private set; }

    public EventType Type { get; private set; }

    public DateTimeOffset ActualAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public EventSource Source { get; private set; }

    public Guid? UserId { get; private set; }

    public string? Remarks { get; private set; }

    /// <summary>The key the caller chose, so a retry returns the first answer instead of recording the event again.</summary>
    public string? ClientKey { get; private set; }

    /// <summary>Set for the events that may happen once per LR (arrival, unloading): the database refuses a second one.</summary>
    public string? OnceKey { get; private set; }

    public Guid? CorrectsEventId { get; private set; }

    // Arrival evidence (only the destination-arrival events carry it).
    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public double? AccuracyM { get; private set; }

    public DateTimeOffset? LocationCapturedAt { get; private set; }

    public double? DestinationLatitude { get; private set; }

    public double? DestinationLongitude { get; private set; }

    public int? RadiusM { get; private set; }

    public double? DistanceM { get; private set; }

    public string? Verification { get; private set; }

    public string? TokenRef { get; private set; }

    public static DeliveryEvent Create(Guid tenantId, Guid? tripId, Guid? lrId, EventType type, DateTimeOffset actualAt, DateTimeOffset recordedAt, EventSource source, Guid? userId, string? remarks,
        string? clientKey, string? tokenRef = null, Guid? corrects = null)
    {
        var onceKey = type is EventType.DestinationArrival or EventType.UnloadingCompleted && lrId is { } id ? $"{id:N}:{type}" : null;
        return new DeliveryEvent
        {
            TenantId = tenantId, TripId = tripId, LrId = lrId, Type = type, ActualAt = actualAt, RecordedAt = recordedAt, Source = source, UserId = userId, Remarks = Customer.Clean(remarks),
            ClientKey = clientKey, OnceKey = onceKey, TokenRef = tokenRef, CorrectsEventId = corrects,
        };
    }

    public DeliveryEvent WithArrivalEvidence(double? latitude, double? longitude, double? accuracyM, DateTimeOffset? capturedAt, double? destLat, double? destLon, int radiusM, double? distanceM, string verification)
    {
        Latitude = latitude;
        Longitude = longitude;
        AccuracyM = accuracyM;
        LocationCapturedAt = capturedAt;
        DestinationLatitude = destLat;
        DestinationLongitude = destLon;
        RadiusM = radiusM;
        DistanceM = distanceM;
        Verification = verification;
        return this;
    }

    /// <summary>An event that is kept but must not block the real one later (a rejected arrival attempt).</summary>
    public DeliveryEvent NotUnique()
    {
        OnceKey = null;
        return this;
    }
}
