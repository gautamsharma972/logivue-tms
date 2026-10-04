namespace Tms.Modules.Transporters.Domain;

public enum KpiType
{
    OnTimePickup = 1,
    OnTimeDelivery = 2,
    PlacementCompliance = 3,
    TenderAcceptance = 4,
    PodCompliance = 5,
    ClaimsRate = 6,
    CostPerformance = 7,
    Availability = 8,
    NoShowRate = 9,
    VehicleReplacementRate = 10,
    PodRejectionRate = 11,
}

/// <summary>Who a delay belongs to. Only <see cref="Carrier"/> delays count against a transporter's KPIs.</summary>
public enum DelayAttribution
{
    None = 0,
    Carrier = 1,
    NonCarrier = 2,
    Unattributed = 3,
}

/// <summary>Operational milestones of a load, in the order they normally occur.</summary>
public enum ExecutionEventType
{
    PickupAppointment = 1,
    VehicleArrival = 2,
    LoadingStart = 3,
    LoadingComplete = 4,
    VehicleDeparture = 5,
    DeliveryArrival = 6,
    UnloadingStart = 7,
    DeliveryComplete = 8,
}

public enum ExecutionStatus
{
    NotStarted = 0,
    AtPickup = 1,
    PickedUp = 2,
    Delivered = 3,
}

public enum InvitationOutcome
{
    Open = 0,
    Accepted = 1,
    Rejected = 2,
}
