namespace Tms.Modules.TransportOps.Domain;

public enum LocationKind { Cfa, Distributor }

public enum ActiveStatus { Active, Inactive }

public enum AgreementStatus { Draft, Active, Superseded, Cancelled }

/// <summary>How the weight that freight is charged on is chosen.</summary>
public enum WeightBasis { Actual, Volumetric, HigherOfActualAndVolumetric }

public enum RateMethod { PerKg, PerTonne, FlatPerConsignment, WeightSlabs }

public enum DetentionUnit { Hour, Day }

/// <summary>Duration: free time is a number of hours/days after arrival. EndOfDay: free until the end of the arrival day (IST), plus any whole free days.</summary>
public enum FreeTimeKind { Duration, EndOfDay }

public enum PartUnitRounding { Exact, RoundUp, RoundDown, Nearest }

public enum TaxMode { None, Forward, ReverseCharge }

public enum RequestStatus { Received, UnderReview, Incomplete, Validated, ReadyForDispatch, Converted, Cancelled }

/// <summary>Where a consignment is on its way. Independent of the POD status.</summary>
public enum DeliveryStatus { Created, Assigned, AtCfa, Loading, Loaded, InTransit, Arrived, UnloadingCompleted, Cancelled }

public enum TripStatus { Planned, AtCfa, Loading, Loaded, InTransit, Completed, Cancelled }

public enum EventType
{
    CfaReached, LoadingStarted, LoadingCompleted, CfaDeparted, DestinationArrival, UnloadingCompleted, PodUploaded, PodPending, ArrivalRejected, ArrivalOverridden, Correction,
}

public enum EventSource { Office, QrWeb, System }

public enum PodStatus { NotYetDue, Pending, Received, UnderReview, Accepted, Rejected }

public enum PodSource { QrWeb, Office }

public enum CalculationStatus { Calculated, OnHold, Reviewed, Invoiced, Superseded }

public enum InvoiceStatus { Draft, PendingApproval, Issued, Cancelled }

public enum SubmissionStatus { NotSubmitted, Submitted, Acknowledged, Rejected, ResubmissionRequired }

public enum PaymentState { Unpaid, PartiallyPaid, Paid }

/// <summary>Why a GPS arrival check came out the way it did.</summary>
public enum ArrivalCheck { Inside, Outside, NoLocation, AccuracyInsufficient, DestinationHasNoCoordinates }
