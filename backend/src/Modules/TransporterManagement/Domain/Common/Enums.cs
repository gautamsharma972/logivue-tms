namespace LogiVue.Tms.TransporterManagement.Domain.Common;

public enum TransporterStatus
{
    Draft,
    Submitted,
    DocumentVerification,
    OperationsReview,
    CommercialReview,
    FinanceReview,
    Approved,
    Active,
    Rejected,
    Suspended,
    Deactivated,
    Blacklisted
}

public enum RecordStatus { Active, Inactive }

public enum DocumentVerificationStatus { Pending, Verified, Rejected }

public enum VehicleOwnershipType { Owned, Attached, Leased, MarketVehicle }

public enum VehicleAvailabilityStatus { Available, Assigned, InTransit, Maintenance, Blocked, Inactive }

public enum RateType { PerTrip, PerKm, PerKg, PerTon, PerCbm, PerShipment }

public enum TenderType { Direct, Sequential, Broadcast }

public enum TenderStatus { Draft, Sent, Viewed, Accepted, Rejected, Expired, Withdrawn, Awarded, Cancelled }

public enum TenderResponseType { Accepted, Rejected, CounterOffered }

public enum PlacementStatus { Requested, Confirmed, VehicleAssigned, Reported, Placed, LoadingStarted, NoShow, Replaced, Cancelled }

public enum KpiType
{
    OnTimePickup,
    OnTimeDelivery,
    PlacementCompliance,
    TenderAcceptance,
    PodCompliance,
    ClaimsRate,
    CostPerformance,
    Availability,
    NoShowRate,
    VehicleReplacementRate,
    PodRejectionRate,
    InvoiceAccuracy,
    DamageRate,
    ShortageRate
}

public enum ScorecardStatus { Generated, Superseded }

public enum Severity { Low, Medium, High, Critical }

public enum AlertStatus { Open, Acknowledged, Resolved }

public enum ExceptionStatus { Open, Acknowledged, InProgress, Resolved, Closed, Escalated }

public enum PlanningRuleType { PreferredCarrier, PreferredLane, AvoidForUrgent, Restricted, DoNotAllocate }

public enum ApprovalActionType { Submit, Advance, Reject, Suspend, Activate, Deactivate, Blacklist }

/// <summary>Operational milestones of a load, in the order they normally occur.</summary>
public enum ExecutionEventType { PickupAppointment, VehicleArrival, LoadingStart, LoadingComplete, VehicleDeparture, DeliveryArrival, UnloadingStart, DeliveryComplete }

public enum ExecutionStatus { NotStarted, AtPickup, PickedUp, Delivered }

/// <summary>Who a delay belongs to. Only <see cref="Carrier"/> delays count against a transporter's KPIs.</summary>
public enum DelayAttribution { None, Carrier, NonCarrier, Unattributed }

public enum PodStatus { Pending, Submitted, UnderReview, Accepted, Rejected, ResubmissionRequired }
