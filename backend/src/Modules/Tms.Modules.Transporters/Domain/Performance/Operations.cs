using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

public enum PlacementStatus
{
    /// <summary>The vehicle is known (it is named when the transporter accepts) and is expected at the pickup.</summary>
    VehicleAssigned = 1,

    /// <summary>The transporter has said the vehicle is on its way / reported.</summary>
    Reported = 2,

    /// <summary>The vehicle is at the pickup site.</summary>
    Placed = 3,

    LoadingStarted = 4,

    NoShow = 5,

    Cancelled = 6,
}

/// <summary>Where a placement stands against its time: OnTime, Late, Pending, Overdue, NoShow or Cancelled.</summary>
public static class PlacementSla
{
    public const string OnTime = "OnTime";
    public const string Late = "Late";
    public const string Pending = "Pending";
    public const string Overdue = "Overdue";
    public const string NoShow = "NoShow";
    public const string Cancelled = "Cancelled";
}

public sealed class PlacementEvent : Entity, ITenantScoped
{
    private PlacementEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PlacementId { get; private set; }

    public string EventType { get; private set; } = null!;

    public DateTimeOffset EventAt { get; private set; }

    public string? Remarks { get; private set; }

    internal static PlacementEvent Create(Guid tenantId, Guid placementId, string type, DateTimeOffset at, string? remarks) =>
        new() { TenantId = tenantId, PlacementId = placementId, EventType = type, EventAt = at, Remarks = remarks };
}

/// <summary>
/// The vehicle a transporter owes at the pickup site for an accepted load, and whether it came on time. The source of the placement,
/// no-show and vehicle-replacement KPIs. Created when the load is accepted; the vehicle named then is the one expected.
/// </summary>
public sealed class VehiclePlacement : AggregateRoot, ITenantScoped
{
    private readonly List<PlacementEvent> _events = [];

    private VehiclePlacement()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string ShipmentNumber { get; private set; } = null!;

    public Guid TransporterId { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    public FreightMode Mode { get; private set; }

    public string? OriginState { get; private set; }

    public string? OriginCity { get; private set; }

    public string? DestinationState { get; private set; }

    public string? DestinationCity { get; private set; }

    public Guid? VehicleId { get; private set; }

    public string? VehicleRegistration { get; private set; }

    public DateTimeOffset RequiredAt { get; private set; }

    public DateTimeOffset? ReportedAt { get; private set; }

    public DateTimeOffset? PlacedAt { get; private set; }

    public DateTimeOffset? LoadingStartedAt { get; private set; }

    public PlacementStatus Status { get; private set; }

    public int ReplacementCount { get; private set; }

    public string? ExceptionReason { get; private set; }

    public IReadOnlyList<PlacementEvent> Events => _events;

    public bool IsOpen => Status is PlacementStatus.VehicleAssigned or PlacementStatus.Reported;

    public bool IsPlaced => Status is PlacementStatus.Placed or PlacementStatus.LoadingStarted;

    public static VehiclePlacement Create(Guid tenantId, ShipmentFact fact, Guid? vehicleId, string? registration, DateTimeOffset requiredAt, DateTimeOffset now)
    {
        var placement = new VehiclePlacement
        {
            TenantId = tenantId, ShipmentId = fact.ShipmentId, ShipmentNumber = fact.Number, TransporterId = fact.TransporterId, VehicleTypeId = fact.VehicleTypeId, Mode = fact.Mode,
            OriginState = fact.OriginState, OriginCity = fact.OriginCity, DestinationState = fact.DestinationState, DestinationCity = fact.DestinationCity,
            VehicleId = vehicleId, VehicleRegistration = registration, RequiredAt = requiredAt, Status = PlacementStatus.VehicleAssigned,
        };
        placement.Log("VehicleAssigned", now, registration);
        return placement;
    }

    public string Sla(DateTimeOffset now, int graceMinutes)
    {
        var deadline = RequiredAt.AddMinutes(graceMinutes);
        return Status switch
        {
            PlacementStatus.NoShow => PlacementSla.NoShow,
            PlacementStatus.Cancelled => PlacementSla.Cancelled,
            _ when IsPlaced => PlacedAt <= deadline ? PlacementSla.OnTime : PlacementSla.Late,
            _ => now > deadline ? PlacementSla.Overdue : PlacementSla.Pending,
        };
    }

    /// <summary>Signed minutes between the required and the actual placement. Positive means late.</summary>
    public int? DelayMinutes => PlacedAt is { } placed ? (int)Math.Round((placed - RequiredAt).TotalMinutes) : null;

    public Result Report(DateTimeOffset now)
    {
        if (Status != PlacementStatus.VehicleAssigned)
        {
            return Illegal("reported");
        }

        ReportedAt = now;
        Status = PlacementStatus.Reported;
        Log("Reported", now, null);
        return Result.Success();
    }

    /// <summary>The vehicle is at the site (said by a person, or by the arrival milestone).</summary>
    public Result Place(DateTimeOffset at)
    {
        if (!IsOpen)
        {
            return Illegal("placed");
        }

        PlacedAt = at;
        Status = PlacementStatus.Placed;
        Log("Placed", at, null);
        return Result.Success();
    }

    public Result StartLoading(DateTimeOffset at)
    {
        if (Status != PlacementStatus.Placed)
        {
            return Illegal("marked as loading");
        }

        LoadingStartedAt = at;
        Status = PlacementStatus.LoadingStarted;
        Log("LoadingStarted", at, null);
        return Result.Success();
    }

    public Result NoShow(string reason, DateTimeOffset now, int graceMinutes)
    {
        if (!IsOpen)
        {
            return Illegal("marked as a no-show");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("placements.reason_required", "Say what happened.");
        }

        if (now <= RequiredAt.AddMinutes(graceMinutes))
        {
            return Error.Conflict("placements.no_show_too_early", "A no-show can be recorded only after the placement grace period has passed.");
        }

        Status = PlacementStatus.NoShow;
        ExceptionReason = reason.Trim();
        Log("NoShow", now, ExceptionReason);
        return Result.Success();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Illegal("cancelled");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("placements.reason_required", "Say why it is cancelled.");
        }

        Status = PlacementStatus.Cancelled;
        ExceptionReason = reason.Trim();
        Log("Cancelled", now, ExceptionReason);
        return Result.Success();
    }

    /// <summary>A different vehicle than the one named. Counts as a replacement; a reported vehicle has to be reported again.</summary>
    public bool Replace(Guid vehicleId, string registration, DateTimeOffset now)
    {
        if (!IsOpen || VehicleId == vehicleId)
        {
            return false;
        }

        VehicleId = vehicleId;
        VehicleRegistration = registration;
        ReplacementCount++;
        Status = PlacementStatus.VehicleAssigned;
        ReportedAt = null;
        Log("VehicleReplaced", now, registration);
        return true;
    }

    private Error Illegal(string action) => Error.Conflict("placements.illegal_transition", $"Placement for {ShipmentNumber} cannot be {action} while it is {Status}.");

    private void Log(string type, DateTimeOffset at, string? remarks) => _events.Add(PlacementEvent.Create(TenantId, Id, type, at, remarks));
}

public enum ClaimType
{
    Damage = 1,
    Shortage = 2,
    LossTheft = 3,
}

public enum ClaimStatus
{
    Open = 1,
    Resolved = 2,
}

/// <summary>
/// A damage, shortage or loss claim against a transporter. Stands in for the Claims module until it exists; the claims KPI reads only these.
/// Claims for a delivery exception are raised automatically (with the value left to fill in), keyed by <see cref="SourceKey"/> so a re-delivered event adds none.
/// </summary>
public sealed class ClaimRecord : AggregateRoot, ITenantScoped
{
    private ClaimRecord()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public Guid? ShipmentId { get; private set; }

    public string? ShipmentNumber { get; private set; }

    public ClaimType ClaimType { get; private set; }

    public DateOnly ClaimDate { get; private set; }

    public decimal ClaimValue { get; private set; }

    public ClaimStatus Status { get; private set; } = ClaimStatus.Open;

    public string? Remarks { get; private set; }

    public string? SourceKey { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public static Result<ClaimRecord> Create(
        Guid tenantId, Guid transporterId, Guid? shipmentId, string? shipmentNumber, ClaimType type, DateOnly date, decimal value, string? remarks, DateOnly today, string? sourceKey = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(type))
        {
            errors["claimType"] = ["Choose the kind of claim."];
        }

        if (value < 0)
        {
            errors["claimValue"] = ["The claim value cannot be negative."];
        }

        if (date > today)
        {
            errors["claimDate"] = ["A claim cannot be dated in the future."];
        }

        if (remarks?.Trim().Length > 500)
        {
            errors["remarks"] = ["Remarks can be at most 500 characters."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        return new ClaimRecord
        {
            TenantId = tenantId, TransporterId = transporterId, ShipmentId = shipmentId, ShipmentNumber = shipmentNumber, ClaimType = type, ClaimDate = date, ClaimValue = value,
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(), SourceKey = sourceKey,
        };
    }

    public Result SetValue(decimal value)
    {
        if (value < 0)
        {
            return Error.Validation("claims.value_invalid", "The claim value cannot be negative.");
        }

        if (Status == ClaimStatus.Resolved)
        {
            return Error.Conflict("claims.resolved", "A resolved claim cannot be changed.");
        }

        ClaimValue = value;
        return Result.Success();
    }

    public Result Resolve(DateTimeOffset now)
    {
        if (Status == ClaimStatus.Resolved)
        {
            return Error.Conflict("claims.already_resolved", "This claim is already resolved.");
        }

        Status = ClaimStatus.Resolved;
        ResolvedAt = now;
        return Result.Success();
    }
}

/// <summary>Agreed and invoiced amount for one load. Cost performance is the share of loads invoiced at or below the agreed amount.</summary>
public sealed class LoadCost : AggregateRoot, ITenantScoped
{
    private LoadCost()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public string ShipmentNumber { get; private set; } = null!;

    public DateOnly ServiceDate { get; private set; }

    public decimal AgreedAmount { get; private set; }

    public decimal InvoicedAmount { get; private set; }

    public bool OnBudget => InvoicedAmount <= AgreedAmount;

    public static Result<LoadCost> Create(Guid tenantId, Guid transporterId, Guid shipmentId, string number, DateOnly serviceDate, decimal agreed, decimal invoiced)
    {
        var errors = new Dictionary<string, string[]>();
        if (agreed <= 0)
        {
            errors["agreedAmount"] = ["The agreed amount must be greater than zero."];
        }

        if (invoiced < 0)
        {
            errors["invoicedAmount"] = ["The invoiced amount cannot be negative."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        return new LoadCost { TenantId = tenantId, TransporterId = transporterId, ShipmentId = shipmentId, ShipmentNumber = number, ServiceDate = serviceDate, AgreedAmount = agreed, InvoicedAmount = invoiced };
    }
}

/// <summary>Vehicles a transporter committed for a day and how many were actually available. Availability is the ratio, summed over the period.</summary>
public sealed class CapacityDay : AggregateRoot, ITenantScoped
{
    private CapacityDay()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TransporterId { get; private set; }

    public DateOnly Date { get; private set; }

    public int VehiclesCommitted { get; private set; }

    public int VehiclesAvailable { get; private set; }

    public static CapacityDay For(Guid tenantId, Guid transporterId, DateOnly date) => new() { TenantId = tenantId, TransporterId = transporterId, Date = date };

    public Result Set(int committed, int available)
    {
        if (committed is < 0 or > 10_000 || available is < 0 or > 10_000)
        {
            return Error.Validation("capacity.range", "Vehicle counts must be between 0 and 10,000.");
        }

        if (available > committed)
        {
            return Error.Validation("capacity.available_exceeds", "Available vehicles cannot exceed committed vehicles.");
        }

        VehiclesCommitted = committed;
        VehiclesAvailable = available;
        return Result.Success();
    }
}

public enum AlertSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

public enum AlertStatus
{
    Open = 1,
    Acknowledged = 2,
    Resolved = 3,
}

/// <summary>Something a planner should look at: a vehicle that did not arrive, a late load, an overdue proof. Raised and resolved by the system; acknowledged and closed by people.</summary>
public sealed class TransporterAlert : AggregateRoot, ITenantScoped
{
    private TransporterAlert()
    {
    }

    public Guid TenantId { get; private set; }

    public string AlertType { get; private set; } = null!;

    public AlertSeverity Severity { get; private set; }

    public Guid TransporterId { get; private set; }

    public Guid? ShipmentId { get; private set; }

    public string? ShipmentNumber { get; private set; }

    /// <summary>What the alert is about, e.g. an execution or a placement, and its key, so the same problem is raised once.</summary>
    public string EntityKey { get; private set; } = null!;

    public string Message { get; private set; } = null!;

    public AlertStatus Status { get; private set; } = AlertStatus.Open;

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? Resolution { get; private set; }

    public static TransporterAlert Raise(Guid tenantId, string type, AlertSeverity severity, Guid transporterId, Guid? shipmentId, string? shipmentNumber, string entityKey, string message) =>
        new() { TenantId = tenantId, AlertType = type, Severity = severity, TransporterId = transporterId, ShipmentId = shipmentId, ShipmentNumber = shipmentNumber, EntityKey = entityKey, Message = message };

    public Result Acknowledge(DateTimeOffset now)
    {
        if (Status != AlertStatus.Open)
        {
            return Error.Conflict("alerts.illegal_transition", $"This alert is {Status} and cannot be acknowledged.");
        }

        Status = AlertStatus.Acknowledged;
        AcknowledgedAt = now;
        return Result.Success();
    }

    public Result Resolve(DateTimeOffset now, string? comments)
    {
        if (Status == AlertStatus.Resolved)
        {
            return Error.Conflict("alerts.already_resolved", "This alert is already resolved.");
        }

        Status = AlertStatus.Resolved;
        ResolvedAt = now;
        Resolution = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        return Result.Success();
    }
}
