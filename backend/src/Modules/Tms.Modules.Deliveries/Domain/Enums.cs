namespace Tms.Modules.Deliveries.Domain;

/// <summary>Where the delivery itself stands. Whether its proof has been accepted is a separate state (<see cref="PodStatus"/>).</summary>
public enum DeliveryStatus
{
    Planned = 1,
    Assigned = 2,
    EnRoute = 3,
    Arrived = 4,

    /// <summary>At least one attempt failed; the vehicle may try again or the delivery may be failed.</summary>
    Attempted = 5,

    /// <summary>Everything was delivered and nothing was short, damaged or refused.</summary>
    Delivered = 6,

    /// <summary>Delivered with a shortage, damage or part of the load not accepted.</summary>
    PartiallyDelivered = 7,

    Refused = 8,
    Failed = 9,

    /// <summary>Proof accepted (or a failed / refused delivery closed by staff).</summary>
    Closed = 10,

    Cancelled = 11,
}

/// <summary>What the driver says happened on completing the delivery.</summary>
public enum DeliveryOutcome
{
    Full = 1,

    /// <summary>Part of the load was not delivered (not accepted or not unloaded) and must be backordered, rescheduled or returned.</summary>
    Partial = 2,

    Shortage = 3,
    Damaged = 4,
    Refused = 5,
    Failed = 6,
}

/// <summary>What happens to the quantity that was not delivered in a partial delivery. Chosen per delivery, never assumed.</summary>
public enum RemainingDisposition
{
    Backorder = 1,
    Reschedule = 2,
    Return = 3,
    Cancel = 4,
    Exception = 5,
}

public enum PodStatus
{
    /// <summary>Delivery done, no proof started. Never stored: reported for a delivery that has no proof record yet.</summary>
    Pending = 1,

    /// <summary>Being put together: quantities and recipient recorded, evidence still coming.</summary>
    Draft = 2,

    /// <summary>All the proof the rules ask for is present.</summary>
    Captured = 3,

    /// <summary>Sent for checking; OCR may still be running.</summary>
    Submitted = 4,

    UnderReview = 5,
    Accepted = 6,
    Rejected = 7,
    ResubmissionRequired = 8,
    Cancelled = 9,
}

public enum ProofMethod
{
    Signature = 1,
    Otp = 2,
    Photo = 3,
    Contactless = 4,
}

public enum EvidenceType
{
    PackagePhoto = 1,
    DamagePhoto = 2,
    LocationPhoto = 3,
    SitePhoto = 4,
    SealPhoto = 5,
    VehiclePhoto = 6,

    /// <summary>A scan or photo of the signed paper POD / delivery challan. OCR reads this one.</summary>
    PodDocument = 7,
}

public enum DiscrepancyType
{
    Shortage = 1,
    Damage = 2,
    Rejection = 3,
}

public enum GeofenceStatus
{
    /// <summary>No geofence is set for the customer, or none is required.</summary>
    NotApplicable = 0,
    Inside = 1,
    Outside = 2,
    GpsUnavailable = 3,
    AccuracyInsufficient = 4,
}

public enum ValidationOutcome
{
    Valid = 1,
    Warning = 2,
    RequiresReview = 3,
    Invalid = 4,
}

public enum ExceptionType
{
    Shortage = 1,
    Damage = 2,
    CustomerRefusal = 3,
    DeliveryFailed = 4,
    LateDelivery = 5,
    AddressIssue = 6,
    PodMissing = 7,
    PodRejected = 8,
    QuantityMismatch = 9,
    GpsException = 10,
    SignatureMissing = 11,
    OcrValidationFailed = 12,
    DuplicatePod = 13,
    PartialDelivery = 14,
}

public enum ExceptionStatus
{
    Open = 1,
    Acknowledged = 2,
    UnderInvestigation = 3,
    ActionRequired = 4,
    Resolved = 5,
    Closed = 6,
    Escalated = 7,
}

public enum ExceptionSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Who is at fault is a finding, never a default.</summary>
public enum ResponsibleParty
{
    Unknown = 0,
    Transporter = 1,
    Warehouse = 2,
    Customer = 3,
    Supplier = 4,
}

public enum OcrStatus
{
    Queued = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
}

public enum OcrFieldStatus
{
    NotChecked = 0,
    Matched = 1,
    Mismatch = 2,
    LowConfidence = 3,
}

public enum SyncStatus
{
    Pending = 1,
    Synced = 2,
    Failed = 3,
    Conflict = 4,
}
