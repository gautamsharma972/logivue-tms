using System.Security.Cryptography;
using System.Text;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Domain;

public sealed record GeoFix(double? Latitude, double? Longitude, double? AccuracyM)
{
    public static readonly GeoFix None = new(null, null, null);

    public bool IsKnown => Geo.IsValid(Latitude, Longitude);

    public static bool IsKnownValue(double? latitude, double? longitude) => Geo.IsValid(latitude, longitude);
}

/// <summary>Who did something and from what device, kept on every event so the timeline can be trusted.</summary>
public sealed record Actor(Guid? UserId, string? DeviceReference);

public enum DeliveryEventType
{
    Created = 1,
    Assigned = 2,
    Started = 3,
    Arrived = 4,
    AttemptFailed = 5,
    Delivered = 6,
    PartiallyDelivered = 7,
    Failed = 8,
    Refused = 9,
    Rescheduled = 10,
    Cancelled = 11,
    Closed = 12,
    OtpIssued = 13,
    OtpVerified = 14,
}

public enum AttemptResult
{
    Failed = 1,
    Delivered = 2,
}

/// <summary>A line of a delivery. Ordered, dispatched and delivered quantities are kept apart so a difference can be explained.</summary>
public sealed class DeliveryItem : Entity, ITenantScoped
{
    private DeliveryItem()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public string SkuReference { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public decimal OrderedQuantity { get; private set; }

    public decimal DispatchedQuantity { get; private set; }

    /// <summary>Null until the driver reports; then delivered, short, damaged and rejected account for the dispatched quantity.</summary>
    public decimal? DeliveredQuantity { get; private set; }

    public decimal ShortQuantity { get; private set; }

    public decimal DamagedQuantity { get; private set; }

    public decimal RejectedQuantity { get; private set; }

    public string UnitOfMeasure { get; private set; } = null!;

    public string? Remarks { get; private set; }

    public string? ShortageReasonCode { get; private set; }

    public string? DamageType { get; private set; }

    public string? DamageReason { get; private set; }

    public string? DamageDescription { get; private set; }

    public bool IsReported => DeliveredQuantity is not null;

    internal static DeliveryItem Create(Guid tenantId, Guid deliveryId, string sku, string description, decimal ordered, decimal dispatched, string unit) => new()
    {
        TenantId = tenantId, DeliveryId = deliveryId, SkuReference = sku.Trim(), Description = description.Trim(), OrderedQuantity = ordered, DispatchedQuantity = dispatched,
        UnitOfMeasure = string.IsNullOrWhiteSpace(unit) ? "UNIT" : unit.Trim().ToUpperInvariant(),
    };

    internal void Record(ItemQuantities q)
    {
        DeliveredQuantity = q.DeliveredQuantity;
        ShortQuantity = q.ShortQuantity;
        DamagedQuantity = q.DamagedQuantity;
        RejectedQuantity = q.RejectedQuantity;
        ShortageReasonCode = q.ShortQuantity > 0 ? q.ShortageReasonCode?.Trim().ToUpperInvariant() : null;
        DamageType = q.DamagedQuantity > 0 ? q.DamageType?.Trim().ToUpperInvariant() : null;
        DamageReason = q.DamagedQuantity > 0 ? q.DamageReason?.Trim() : null;
        DamageDescription = q.DamagedQuantity > 0 ? q.DamageDescription?.Trim() : null;
        Remarks = string.IsNullOrWhiteSpace(q.Remarks) ? null : q.Remarks.Trim();
    }

    internal void RecordRefusal()
    {
        DeliveredQuantity = 0;
        ShortQuantity = 0;
        DamagedQuantity = 0;
        RejectedQuantity = DispatchedQuantity;
    }
}

public sealed class DeliveryAttempt : Entity, ITenantScoped
{
    private DeliveryAttempt()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public int AttemptNumber { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    public AttemptResult Result { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? RecipientName { get; private set; }

    public string? DriverRemarks { get; private set; }

    public string? CustomerRemarks { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public double? GpsAccuracy { get; private set; }

    public string? DeviceReference { get; private set; }

    internal static DeliveryAttempt Create(Guid tenantId, Guid deliveryId, int number, DateTimeOffset at, AttemptResult result, string? reason, string? recipient, string? driverRemarks, string? customerRemarks, GeoFix fix, string? device) => new()
    {
        TenantId = tenantId, DeliveryId = deliveryId, AttemptNumber = number, AttemptedAt = at, Result = result, ReasonCode = reason?.Trim().ToUpperInvariant(),
        RecipientName = Clean(recipient), DriverRemarks = Clean(driverRemarks), CustomerRemarks = Clean(customerRemarks),
        Latitude = fix.IsKnown ? fix.Latitude : null, Longitude = fix.IsKnown ? fix.Longitude : null, GpsAccuracy = fix.IsKnown ? fix.AccuracyM : null, DeviceReference = Clean(device),
    };

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

public sealed class DeliveryEvent : Entity, ITenantScoped
{
    private DeliveryEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public DeliveryEventType EventType { get; private set; }

    public DateTimeOffset EventAt { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public Guid? PerformedBy { get; private set; }

    public string? DeviceReference { get; private set; }

    public string? Remarks { get; private set; }

    internal static DeliveryEvent Create(Guid tenantId, Guid deliveryId, DeliveryEventType type, DateTimeOffset at, GeoFix fix, Actor actor, string? remarks) => new()
    {
        TenantId = tenantId, DeliveryId = deliveryId, EventType = type, EventAt = at, Latitude = fix.IsKnown ? fix.Latitude : null, Longitude = fix.IsKnown ? fix.Longitude : null,
        PerformedBy = actor.UserId, DeviceReference = actor.DeviceReference, Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
    };
}

/// <summary>A shortage, damage or rejected quantity found at the drop, with the reason and whether the customer acknowledged it.</summary>
public sealed class DeliveryDiscrepancy : Entity, ITenantScoped
{
    private DeliveryDiscrepancy()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public Guid DeliveryItemId { get; private set; }

    public DiscrepancyType Type { get; private set; }

    public decimal Quantity { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? Description { get; private set; }

    public bool CustomerAcknowledged { get; private set; }

    public string? ClaimReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static DeliveryDiscrepancy Create(Guid tenantId, Guid deliveryId, Guid itemId, DiscrepancyType type, decimal quantity, string? reason, string? description, DateTimeOffset now) => new()
    {
        TenantId = tenantId, DeliveryId = deliveryId, DeliveryItemId = itemId, Type = type, Quantity = quantity, ReasonCode = reason, Description = description, CreatedAt = now,
    };

    internal void Acknowledge() => CustomerAcknowledged = true;

    public void LinkClaim(string reference) => ClaimReference = reference;
}

/// <param name="Reconciliation">One line per item, with the exact difference when quantities do not add up.</param>
public sealed record CompletionResult(IReadOnlyList<ReconciliationResult> Reconciliation, bool HasDiscrepancy, bool HasMismatch);

/// <summary>What a driver is sent to deliver and where, with the state of the attempt. Whether the proof has been accepted lives on the POD, not here.</summary>
public sealed class Delivery : AggregateRoot, ITenantScoped
{
    public const int MaxAttempts = 10;

    private readonly List<DeliveryItem> _items = [];
    private readonly List<DeliveryAttempt> _attempts = [];
    private readonly List<DeliveryEvent> _events = [];
    private readonly List<DeliveryDiscrepancy> _discrepancies = [];

    private Delivery()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public DeliveryStatus Status { get; private set; }

    public DeliveryOutcome? Outcome { get; private set; }

    public RemainingDisposition? RemainingDisposition { get; private set; }

    // References to the world outside this module: nothing here points into another module's tables.
    public Guid? ShipmentId { get; private set; }

    public string? ShipmentReference { get; private set; }

    public Guid? OrderId { get; private set; }

    public string? OrderReference { get; private set; }

    public string? LoadReference { get; private set; }

    public string? TripReference { get; private set; }

    public string? LrNumber { get; private set; }

    public int Sequence { get; private set; }

    public Guid? TransporterId { get; private set; }

    public string? TransporterReference { get; private set; }

    public Guid? VehicleId { get; private set; }

    public string? VehicleReference { get; private set; }

    public string? DriverName { get; private set; }

    /// <summary>How the load moves: FTL, PTL or Dedicated. Comes from the shipment; used to filter reports.</summary>
    public string? ServiceType { get; private set; }

    public string? CustomerReference { get; private set; }

    public string CustomerName { get; private set; } = null!;

    public string? CustomerPhone { get; private set; }

    public string? CustomerEmail { get; private set; }

    public string? OriginReference { get; private set; }

    public string? DestinationReference { get; private set; }

    public string? DestinationAddress { get; private set; }

    public double? CustomerLatitude { get; private set; }

    public double? CustomerLongitude { get; private set; }

    /// <summary>Allowed distance of the delivery fix from the customer. Null: no geofence for this customer.</summary>
    public int? GeofenceRadiusM { get; private set; }

    public DateTimeOffset PlannedDeliveryAt { get; private set; }

    public DateTimeOffset? WindowStart { get; private set; }

    public DateTimeOffset? WindowEnd { get; private set; }

    public DateTimeOffset? ActualArrivalAt { get; private set; }

    public DateTimeOffset? ActualDeliveryAt { get; private set; }

    public double? ArrivalLatitude { get; private set; }

    public double? ArrivalLongitude { get; private set; }

    public double? ArrivalAccuracy { get; private set; }

    public bool HasQuantityMismatch { get; private set; }

    // One-time code the customer reads out. Only a salted hash is kept, never the code.
    public string? OtpHash { get; private set; }

    public string? OtpSalt { get; private set; }

    public DateTimeOffset? OtpExpiresAt { get; private set; }

    public int OtpAttempts { get; private set; }

    public DateTimeOffset? OtpVerifiedAt { get; private set; }

    public IReadOnlyList<DeliveryItem> Items => _items;

    public IReadOnlyList<DeliveryAttempt> Attempts => _attempts;

    public IReadOnlyList<DeliveryEvent> Events => _events;

    public IReadOnlyList<DeliveryDiscrepancy> Discrepancies => _discrepancies;

    public bool IsActive => Status is not (DeliveryStatus.Closed or DeliveryStatus.Cancelled);

    public bool IsCompleted => Status is DeliveryStatus.Delivered or DeliveryStatus.PartiallyDelivered;

    public bool OtpVerified => OtpVerifiedAt is not null;

    /// <summary>Delivered within the window; null when there is no window or nothing was delivered.</summary>
    public bool? OnTime => WindowEnd is { } end && ActualDeliveryAt is { } at && Outcome is not (DeliveryOutcome.Failed or DeliveryOutcome.Refused) ? at <= end : null;

    public sealed record Header(
        Guid? ShipmentId, string? ShipmentReference, Guid? OrderId, string? OrderReference, string? LoadReference, string? TripReference, string? LrNumber, int Sequence,
        Guid? TransporterId, string? TransporterReference, Guid? VehicleId, string? VehicleReference, string? DriverName,
        string? CustomerReference, string CustomerName, string? CustomerPhone, string? CustomerEmail,
        string? OriginReference, string? DestinationReference, string? DestinationAddress,
        double? CustomerLatitude, double? CustomerLongitude, int? GeofenceRadiusM,
        DateTimeOffset PlannedDeliveryAt, DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, string? ServiceType = null);

    public sealed record ItemInput(string Sku, string Description, decimal Ordered, decimal Dispatched, string? Unit);

    public static Result<Delivery> Create(Guid tenantId, string number, Header h, IReadOnlyList<ItemInput> items, Actor actor, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(h.CustomerName))
        {
            errors["customerName"] = ["Enter the customer."];
        }

        if (items.Count == 0)
        {
            errors["items"] = ["A delivery needs at least one item."];
        }
        else if (items.Any(i => string.IsNullOrWhiteSpace(i.Sku) || i.Ordered < 0 || i.Dispatched < 0 || i.Dispatched == 0 && i.Ordered == 0))
        {
            errors["items"] = ["Every item needs a SKU and a quantity above zero."];
        }

        if (h.WindowStart is { } start && h.WindowEnd is { } end && end < start)
        {
            errors["windowEnd"] = ["The delivery window must end after it starts."];
        }

        if (h.GeofenceRadiusM is <= 0 || (h.GeofenceRadiusM is not null && !Geo.IsValid(h.CustomerLatitude, h.CustomerLongitude)))
        {
            errors["geofenceRadiusM"] = ["A geofence needs the customer's coordinates and a radius above zero."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("deliveries.invalid", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        var delivery = new Delivery { TenantId = tenantId, Number = number, Status = h.TransporterId is null ? DeliveryStatus.Planned : DeliveryStatus.Assigned };
        delivery.ApplyHeader(h);
        foreach (var i in items)
        {
            delivery._items.Add(DeliveryItem.Create(tenantId, delivery.Id, i.Sku, i.Description, i.Ordered, i.Dispatched, i.Unit ?? "UNIT"));
        }

        delivery.Log(DeliveryEventType.Created, now, GeoFix.None, actor, null);
        if (delivery.Status == DeliveryStatus.Assigned)
        {
            delivery.Log(DeliveryEventType.Assigned, now, GeoFix.None, actor, delivery.TransporterReference);
        }

        return delivery;
    }

    /// <summary>Changes what is planned for a delivery that has not started.</summary>
    public Result Update(Header h, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Planned or DeliveryStatus.Assigned))
        {
            return Error.Conflict("deliveries.locked", "A delivery that is under way or finished can no longer be changed.");
        }

        if (string.IsNullOrWhiteSpace(h.CustomerName))
        {
            return Error.Validation("deliveries.invalid", "Enter the customer.");
        }

        ApplyHeader(h);
        Status = TransporterId is null ? DeliveryStatus.Planned : DeliveryStatus.Assigned;
        return Result.Success();
    }

    public Result Assign(Guid transporterId, string? transporterReference, Guid? vehicleId, string? vehicleReference, string? driverName, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Planned or DeliveryStatus.Assigned))
        {
            return Error.Conflict("deliveries.locked", "Only a delivery that has not started can be assigned.");
        }

        TransporterId = transporterId;
        TransporterReference = transporterReference;
        VehicleId = vehicleId;
        VehicleReference = vehicleReference;
        DriverName = driverName;
        Status = DeliveryStatus.Assigned;
        Log(DeliveryEventType.Assigned, now, GeoFix.None, actor, vehicleReference);
        return Result.Success();
    }

    public Result Start(GeoFix fix, Actor actor, DateTimeOffset now)
    {
        if (Status != DeliveryStatus.Assigned)
        {
            return Transition("start", "assigned");
        }

        Status = DeliveryStatus.EnRoute;
        Log(DeliveryEventType.Started, now, fix, actor, null);
        return Result.Success();
    }

    public Result Arrive(GeoFix fix, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.EnRoute or DeliveryStatus.Attempted))
        {
            return Transition("record the arrival of", "en route");
        }

        Status = DeliveryStatus.Arrived;
        ActualArrivalAt ??= now;
        if (fix.IsKnown)
        {
            ArrivalLatitude = fix.Latitude;
            ArrivalLongitude = fix.Longitude;
            ArrivalAccuracy = fix.AccuracyM;
        }

        Log(DeliveryEventType.Arrived, now, fix, actor, null);
        return Result.Success();
    }

    /// <summary>A visit that did not end in delivery (customer closed, address wrong…). The delivery stays open for another try.</summary>
    public Result RecordFailedAttempt(string reasonCode, string? driverRemarks, string? customerRemarks, string? recipient, GeoFix fix, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Arrived or DeliveryStatus.Attempted))
        {
            return Transition("record an attempt on", "arrived");
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return ReasonRequired("Say why the delivery could not be made.");
        }

        if (_attempts.Count >= MaxAttempts)
        {
            return Error.Conflict("deliveries.too_many_attempts", $"A delivery can have at most {MaxAttempts} attempts. Fail it or reschedule it.");
        }

        _attempts.Add(DeliveryAttempt.Create(TenantId, Id, _attempts.Count + 1, now, AttemptResult.Failed, reasonCode, recipient, driverRemarks, customerRemarks, fix, actor.DeviceReference));
        Status = DeliveryStatus.Attempted;
        Log(DeliveryEventType.AttemptFailed, now, fix, actor, reasonCode.Trim().ToUpperInvariant());
        return Result.Success();
    }

    public Result Fail(string reasonCode, string? remarks, GeoFix fix, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Arrived or DeliveryStatus.Attempted))
        {
            return Transition("fail", "arrived");
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return ReasonRequired("Say why the delivery failed.");
        }

        _attempts.Add(DeliveryAttempt.Create(TenantId, Id, _attempts.Count + 1, now, AttemptResult.Failed, reasonCode, null, remarks, null, fix, actor.DeviceReference));
        Status = DeliveryStatus.Failed;
        Outcome = DeliveryOutcome.Failed;
        Log(DeliveryEventType.Failed, now, fix, actor, reasonCode.Trim().ToUpperInvariant());
        return Result.Success();
    }

    /// <summary>The customer would not accept the delivery. Every item is recorded as rejected.</summary>
    public Result Refuse(string reasonCode, string? recipient, string? remarks, GeoFix fix, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Arrived or DeliveryStatus.Attempted))
        {
            return Transition("record a refusal on", "arrived");
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return ReasonRequired("Say why the customer refused.");
        }

        foreach (var item in _items)
        {
            item.RecordRefusal();
            _discrepancies.Add(DeliveryDiscrepancy.Create(TenantId, Id, item.Id, DiscrepancyType.Rejection, item.DispatchedQuantity, reasonCode.Trim().ToUpperInvariant(), remarks, now));
        }

        _attempts.Add(DeliveryAttempt.Create(TenantId, Id, _attempts.Count + 1, now, AttemptResult.Failed, reasonCode, recipient, remarks, null, fix, actor.DeviceReference));
        Status = DeliveryStatus.Refused;
        Outcome = DeliveryOutcome.Refused;
        ActualDeliveryAt = now;
        Log(DeliveryEventType.Refused, now, fix, actor, reasonCode.Trim().ToUpperInvariant());
        return Result.Success();
    }

    /// <summary>
    /// Records what was actually delivered. The quantities are checked and reported exactly as given: a difference is raised, never absorbed.
    /// </summary>
    public Result<CompletionResult> Complete(
        DeliveryOutcome outcome, IReadOnlyList<ItemQuantities> quantities, RemainingDisposition? disposition, DateTimeOffset deliveredAt, string? driverRemarks, string? recipient,
        GeoFix fix, QuantityRulesSetting rules, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Arrived or DeliveryStatus.Attempted))
        {
            return Transition("complete", "arrived");
        }

        if (outcome is not (DeliveryOutcome.Full or DeliveryOutcome.Partial or DeliveryOutcome.Shortage or DeliveryOutcome.Damaged))
        {
            return Error.Validation("deliveries.outcome_invalid", "Complete a delivery as delivered in full, partial, short or damaged. Use the refuse or fail actions otherwise.");
        }

        var byItem = quantities.GroupBy(q => q.ItemId).ToDictionary(g => g.Key, g => g.Last());
        if (_items.Any(i => !byItem.ContainsKey(i.Id)) || byItem.Keys.Any(id => _items.All(i => i.Id != id)))
        {
            return Error.Validation("deliveries.items_mismatch", "Report the quantities of every item on the delivery, and only of those.");
        }

        var reconciliation = _items.Select(i => QuantityReconciliation.Check(i, byItem[i.Id], rules)).ToList();
        var invalid = reconciliation.Where(r => r.Problems.Count > 0 && (byItem[r.ItemId].DeliveredQuantity < 0 || byItem[r.ItemId].ShortQuantity < 0 || byItem[r.ItemId].DamagedQuantity < 0 || byItem[r.ItemId].RejectedQuantity < 0 || r.Problems.Any(p => p.StartsWith("Delivered ", StringComparison.Ordinal)))).ToList();
        if (invalid.Count > 0)
        {
            return Error.Validation("deliveries.quantity_invalid", string.Join(" ", invalid.SelectMany(r => r.Problems)));
        }

        var mismatch = reconciliation.Any(r => !r.Reconciled);
        if (mismatch && rules.BlockUnreconciledCompletion)
        {
            return Error.Validation("deliveries.quantities_do_not_add_up", string.Join(" ", reconciliation.SelectMany(r => r.Problems)));
        }

        var shortTotal = quantities.Sum(q => q.ShortQuantity);
        var damagedTotal = quantities.Sum(q => q.DamagedQuantity);
        var rejectedTotal = quantities.Sum(q => q.RejectedQuantity);
        var anyDiscrepancy = shortTotal + damagedTotal + rejectedTotal > 0;

        var consistent = outcome switch
        {
            DeliveryOutcome.Full => !anyDiscrepancy,
            DeliveryOutcome.Shortage => shortTotal > 0,
            DeliveryOutcome.Damaged => damagedTotal > 0,
            DeliveryOutcome.Partial => rejectedTotal > 0,
            _ => false,
        };
        if (!consistent)
        {
            return Error.Validation("deliveries.outcome_mismatch", outcome == DeliveryOutcome.Full
                ? "The quantities show a shortage, damage or rejected goods, so this was not delivered in full."
                : $"The outcome '{outcome}' does not match the quantities reported.");
        }

        foreach (var q in quantities.Where(q => q.ShortQuantity > 0 && string.IsNullOrWhiteSpace(q.ShortageReasonCode)))
        {
            return Error.Validation("deliveries.shortage_reason_required", $"Give a reason for the shortage on {_items.First(i => i.Id == q.ItemId).SkuReference}.");
        }

        foreach (var q in quantities.Where(q => q.DamagedQuantity > 0 && (string.IsNullOrWhiteSpace(q.DamageType) || string.IsNullOrWhiteSpace(q.DamageReason))))
        {
            return Error.Validation("deliveries.damage_reason_required", $"Give the type of damage and the reason for {_items.First(i => i.Id == q.ItemId).SkuReference}.");
        }

        if (rejectedTotal > 0 && disposition is null)
        {
            return Error.Validation("deliveries.disposition_required", "Say what happens to the goods that were not delivered: backorder, reschedule, return, cancel or an exception.");
        }

        foreach (var item in _items)
        {
            item.Record(byItem[item.Id]);
        }

        _discrepancies.Clear();
        foreach (var item in _items)
        {
            if (item.ShortQuantity > 0)
            {
                _discrepancies.Add(DeliveryDiscrepancy.Create(TenantId, Id, item.Id, DiscrepancyType.Shortage, item.ShortQuantity, item.ShortageReasonCode, item.Remarks, now));
            }

            if (item.DamagedQuantity > 0)
            {
                _discrepancies.Add(DeliveryDiscrepancy.Create(TenantId, Id, item.Id, DiscrepancyType.Damage, item.DamagedQuantity, item.DamageType, item.DamageDescription ?? item.DamageReason, now));
            }

            if (item.RejectedQuantity > 0)
            {
                _discrepancies.Add(DeliveryDiscrepancy.Create(TenantId, Id, item.Id, DiscrepancyType.Rejection, item.RejectedQuantity, null, item.Remarks, now));
            }
        }

        Outcome = outcome;
        RemainingDisposition = rejectedTotal > 0 ? disposition : null;
        HasQuantityMismatch = mismatch;
        ActualDeliveryAt = deliveredAt;
        Status = anyDiscrepancy ? DeliveryStatus.PartiallyDelivered : DeliveryStatus.Delivered;
        _attempts.Add(DeliveryAttempt.Create(TenantId, Id, _attempts.Count + 1, deliveredAt, AttemptResult.Delivered, null, recipient, driverRemarks, null, fix, actor.DeviceReference));
        Log(anyDiscrepancy ? DeliveryEventType.PartiallyDelivered : DeliveryEventType.Delivered, deliveredAt, fix, actor, outcome.ToString());
        Raise(new DeliveryCompleted(Id, TenantId, Number, ShipmentId, TransporterId, deliveredAt, anyDiscrepancy, OnTime, shortTotal, damagedTotal, OrderId));
        if (anyDiscrepancy)
        {
            Raise(new DeliveryPartiallyCompleted(Id, TenantId, Number, ShipmentId, TransporterId, deliveredAt, outcome.ToString()));
        }

        if (shortTotal > 0)
        {
            Raise(new ShortageRecorded(Id, TenantId, Number, ShipmentId, TransporterId, deliveredAt, shortTotal));
        }

        if (damagedTotal > 0)
        {
            Raise(new DamageRecorded(Id, TenantId, Number, ShipmentId, TransporterId, deliveredAt, damagedTotal));
        }
        return new CompletionResult(reconciliation, anyDiscrepancy, mismatch);
    }

    /// <summary>A failed or refused delivery goes back to Assigned for another day. Earlier attempts stay on record.</summary>
    public Result Reschedule(DateTimeOffset plannedAt, DateTimeOffset? windowStart, DateTimeOffset? windowEnd, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Failed or DeliveryStatus.Refused or DeliveryStatus.Attempted or DeliveryStatus.Arrived))
        {
            return Error.Conflict("deliveries.not_reschedulable", "Only a delivery that could not be made can be rescheduled.");
        }

        if (windowStart is { } s && windowEnd is { } e && e < s)
        {
            return Error.Validation("deliveries.invalid", "The delivery window must end after it starts.");
        }

        PlannedDeliveryAt = plannedAt;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        Outcome = null;
        ActualArrivalAt = null;
        ActualDeliveryAt = null;
        foreach (var item in _items.Where(i => i.IsReported))
        {
            item.Record(new ItemQuantities(item.Id, 0, 0, 0, 0));
        }

        _discrepancies.Clear();
        Status = TransporterId is null ? DeliveryStatus.Planned : DeliveryStatus.Assigned;
        Log(DeliveryEventType.Rescheduled, now, GeoFix.None, actor, null);
        return Result.Success();
    }

    public Result Cancel(string reason, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Planned or DeliveryStatus.Assigned or DeliveryStatus.EnRoute))
        {
            return Error.Conflict("deliveries.not_cancellable", "Only a delivery that has not reached the customer can be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ReasonRequired("Say why the delivery is being cancelled.");
        }

        Status = DeliveryStatus.Cancelled;
        Log(DeliveryEventType.Cancelled, now, GeoFix.None, actor, reason.Trim());
        return Result.Success();
    }

    /// <summary>The proof was accepted: the delivery is done. A failed or refused delivery is closed by staff with a reason.</summary>
    public Result Close(string? reason, Actor actor, DateTimeOffset now)
    {
        if (Status is DeliveryStatus.Failed or DeliveryStatus.Refused && string.IsNullOrWhiteSpace(reason))
        {
            return ReasonRequired("Say why the delivery is being closed.");
        }

        if (Status is not (DeliveryStatus.Delivered or DeliveryStatus.PartiallyDelivered or DeliveryStatus.Failed or DeliveryStatus.Refused))
        {
            return Error.Conflict("deliveries.not_closable", "Only a completed, failed or refused delivery can be closed.");
        }

        Status = DeliveryStatus.Closed;
        Log(DeliveryEventType.Closed, now, GeoFix.None, actor, reason);
        return Result.Success();
    }

    public void AcknowledgeDiscrepancies()
    {
        foreach (var d in _discrepancies)
        {
            d.Acknowledge();
        }
    }

    /// <summary>Remembers a fresh one-time code (as a salted hash) for the customer to read out on arrival.</summary>
    public void IssueOtp(string code, int validityMinutes, Actor actor, DateTimeOffset now)
    {
        OtpSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        OtpHash = HashOtp(code, OtpSalt);
        OtpExpiresAt = now.AddMinutes(validityMinutes);
        OtpAttempts = 0;
        OtpVerifiedAt = null;
        Log(DeliveryEventType.OtpIssued, now, GeoFix.None, actor, null);
    }

    public bool HasOtpChallenge => OtpHash is not null;

    public Result VerifyOtp(string code, int maxAttempts, Actor actor, DateTimeOffset now)
    {
        if (Status is not (DeliveryStatus.Arrived or DeliveryStatus.Attempted))
        {
            return Transition("verify a code on", "arrived");
        }

        if (OtpHash is null || OtpSalt is null)
        {
            return Error.Conflict("deliveries.no_otp", "No code has been issued for this delivery.");
        }

        if (OtpVerifiedAt is not null)
        {
            return Result.Success();
        }

        if (OtpAttempts >= maxAttempts)
        {
            return Error.Conflict("deliveries.otp_locked", "Too many wrong codes. Ask for a new code.");
        }

        if (OtpExpiresAt is { } expires && now > expires)
        {
            return Error.Conflict("deliveries.otp_expired", "The code has expired. Ask for a new code.");
        }

        OtpAttempts++;
        var given = HashOtp(code.Trim(), OtpSalt);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(OtpHash)))
        {
            return Error.Validation("deliveries.otp_wrong", "That code is not right.");
        }

        OtpVerifiedAt = now;
        Log(DeliveryEventType.OtpVerified, now, GeoFix.None, actor, null);
        return Result.Success();
    }

    private static string HashOtp(string code, string salt)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(salt));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(code)));
    }

    private void ApplyHeader(Header h)
    {
        ShipmentId = h.ShipmentId;
        ShipmentReference = Clean(h.ShipmentReference);
        OrderId = h.OrderId;
        OrderReference = Clean(h.OrderReference);
        LoadReference = Clean(h.LoadReference);
        TripReference = Clean(h.TripReference);
        LrNumber = Clean(h.LrNumber);
        Sequence = h.Sequence;
        TransporterId = h.TransporterId;
        TransporterReference = Clean(h.TransporterReference);
        VehicleId = h.VehicleId;
        VehicleReference = Clean(h.VehicleReference);
        DriverName = Clean(h.DriverName);
        ServiceType = Clean(h.ServiceType);
        CustomerReference = Clean(h.CustomerReference);
        CustomerName = h.CustomerName.Trim();
        CustomerPhone = Clean(h.CustomerPhone);
        CustomerEmail = Clean(h.CustomerEmail);
        OriginReference = Clean(h.OriginReference);
        DestinationReference = Clean(h.DestinationReference);
        DestinationAddress = Clean(h.DestinationAddress);
        CustomerLatitude = Geo.IsValid(h.CustomerLatitude, h.CustomerLongitude) ? h.CustomerLatitude : null;
        CustomerLongitude = CustomerLatitude is null ? null : h.CustomerLongitude;
        GeofenceRadiusM = CustomerLatitude is null ? null : h.GeofenceRadiusM;
        PlannedDeliveryAt = h.PlannedDeliveryAt;
        WindowStart = h.WindowStart;
        WindowEnd = h.WindowEnd;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private void Log(DeliveryEventType type, DateTimeOffset at, GeoFix fix, Actor actor, string? remarks)
    {
        _events.Add(DeliveryEvent.Create(TenantId, Id, type, at, fix, actor, remarks));

        // Every step that matters to another module is also published. Delivered / PartiallyDelivered are covered by DeliveryCompleted and the events raised in Complete.
        DomainEvent? published = type switch
        {
            DeliveryEventType.Assigned => new DeliveryAssigned(Id, TenantId, Number, ShipmentId, TransporterId, at, remarks),
            DeliveryEventType.Started => new DeliveryStarted(Id, TenantId, Number, ShipmentId, TransporterId, at),
            DeliveryEventType.Arrived => new VehicleArrived(Id, TenantId, Number, ShipmentId, TransporterId, at),
            DeliveryEventType.AttemptFailed => new DeliveryAttempted(Id, TenantId, Number, ShipmentId, TransporterId, at, remarks),
            DeliveryEventType.Failed => new DeliveryFailed(Id, TenantId, Number, ShipmentId, TransporterId, at, remarks),
            DeliveryEventType.Refused => new CustomerRefused(Id, TenantId, Number, ShipmentId, TransporterId, at, remarks),
            _ => null,
        };
        if (published is not null)
        {
            Raise(published);
        }
    }

    private Error Transition(string action, string needed) =>
        Error.Conflict("deliveries.invalid_state", $"Cannot {action} a delivery that is {Status}. It must be {needed} first.");

    private static Error ReasonRequired(string message) =>
        Error.Validation("deliveries.reason_required", message) with { ValidationErrors = new Dictionary<string, string[]> { ["reason"] = [message] } };
}
