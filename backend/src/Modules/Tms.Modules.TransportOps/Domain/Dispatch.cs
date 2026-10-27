using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Domain;

public sealed class LrLine : Entity, ITenantScoped
{
    private LrLine()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid LrId { get; private set; }

    public string? MaterialCode { get; private set; }

    public string? MaterialName { get; private set; }

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = "PKG";

    public decimal WeightKg { get; private set; }

    public decimal? VolumeCbm { get; private set; }

    public string? Dimensions { get; private set; }

    public int Packages { get; private set; }

    /// <summary>Volume as the office quotes it, in cubic feet. <see cref="VolumeCbm"/> is kept in step with it because freight rating works in cubic metres.</summary>
    public decimal? VolumeCuFt { get; private set; }

    public decimal? NetWeightKg { get; private set; }

    /// <summary>The customer's invoice number for the goods on this line.</summary>
    public string? InvoiceNo { get; private set; }

    public const decimal CubicMetresPerCubicFoot = 0.0283168466m;

    public static LrLine Create(Guid tenantId, Guid lrId, string? code, string? name, decimal quantity, string unit, decimal weightKg, decimal? volumeCbm, string? dimensions, int packages,
        decimal? volumeCuFt = null, decimal? netWeightKg = null, string? invoiceNo = null) => new()
    {
        TenantId = tenantId, LrId = lrId, MaterialCode = code, MaterialName = string.IsNullOrWhiteSpace(name) ? null : name.Trim(), Quantity = quantity, Unit = unit, WeightKg = weightKg,
        VolumeCbm = volumeCuFt is { } cuFt ? Math.Round(cuFt * CubicMetresPerCubicFoot, 3) : volumeCbm, Dimensions = dimensions, Packages = packages, VolumeCuFt = volumeCuFt, NetWeightKg = netWeightKg, InvoiceNo = string.IsNullOrWhiteSpace(invoiceNo) ? null : invoiceNo.Trim(),
    };
}

/// <summary>
/// A lorry receipt: one consignment to one destination, with the secret that its printed QR code opens. The QR carries only the secret; the secret is shown once and only its hash
/// is stored, so a copy of the database cannot reveal working codes.
/// </summary>
public sealed class Lr : AggregateRoot, ITenantScoped
{
    private Lr()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public Guid? RequestId { get; private set; }

    public string? CustomerReference { get; private set; }

    /// <summary>The customer's indent number for this consignment.</summary>
    public string? IndentNo { get; private set; }

    public MovementType Movement { get; private set; } = MovementType.Supply;

    /// <summary>Only for <see cref="MovementType.CrossBilling"/>: how the freight is priced and the amounts typed in.</summary>
    public CrossBillingMode? CrossMode { get; private set; }

    public decimal? CrossAmount { get; private set; }

    public decimal? CrossKg { get; private set; }

    public decimal? CrossRatePerKg { get; private set; }

    /// <summary>The agreed freight for a cross-billing LR: the lumpsum, or kilograms times the rate.</summary>
    public decimal? CrossFreight => CrossMode switch
    {
        CrossBillingMode.Lumpsum => CrossAmount is { } a ? Money.Round(a) : null,
        CrossBillingMode.PerKg when CrossKg is { } kg && CrossRatePerKg is { } rate => Money.Round(kg * rate),
        _ => null,
    };

    public Guid CfaLocationId { get; private set; }

    public Guid DestinationLocationId { get; private set; }

    public Guid? TripId { get; private set; }

    public Guid? TripStopId { get; private set; }

    public string? VehicleRegistration { get; private set; }

    public DateOnly? DeliveryDate { get; private set; }

    public string? Instructions { get; private set; }

    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Created;

    public decimal TotalWeightKg { get; private set; }

    public decimal? TotalVolumeCbm { get; private set; }

    public int TotalPackages { get; private set; }

    public List<LrLine> Lines { get; private set; } = [];

    public DateTimeOffset? CfaReachedAt { get; private set; }

    public DateTimeOffset? LoadingStartedAt { get; private set; }

    public DateTimeOffset? LoadingCompletedAt { get; private set; }

    public DateTimeOffset? DepartedAt { get; private set; }

    public DateTimeOffset? ArrivedAt { get; private set; }

    public DateTimeOffset? UnloadedAt { get; private set; }

    [AuditIgnore]
    public string? TokenHash { get; private set; }

    /// <summary>The same secret, encrypted, so the office can reprint the LR with the code drivers already have. Only the hash is ever used to look a code up.</summary>
    [AuditIgnore]
    public string? TokenCipher { get; private set; }

    public DateTimeOffset? TokenIssuedAt { get; private set; }

    public DateTimeOffset? TokenExpiresAt { get; private set; }

    public DateTimeOffset? TokenRevokedAt { get; private set; }

    public int PrintCount { get; private set; }

    public bool TokenActiveAt(DateTimeOffset now) => TokenHash is not null && TokenRevokedAt is null && TokenExpiresAt is { } expires && now < expires;

    /// <summary>A short, non-secret reference to the code that was used, kept on events so an action can be traced to the printout it came from.</summary>
    public string? TokenRef => TokenHash?[..12];

    public static Lr Create(Guid tenantId, string number, Guid customerId, Guid? requestId, string? customerReference, Guid cfa, Guid destination, DateOnly? deliveryDate, string? instructions,
        IEnumerable<(string? Code, string? Name, decimal Quantity, string Unit, decimal WeightKg, decimal? VolumeCbm, string? Dimensions, int Packages, decimal? VolumeCuFt, decimal? NetWeightKg, string? InvoiceNo)> lines,
        string? indentNo = null, MovementType movement = MovementType.Supply, CrossBillingMode? crossMode = null, decimal? crossAmount = null, decimal? crossKg = null, decimal? crossRatePerKg = null)
    {
        var lr = new Lr
        {
            TenantId = tenantId, Number = number, CustomerId = customerId, RequestId = requestId, CustomerReference = customerReference, CfaLocationId = cfa, DestinationLocationId = destination,
            DeliveryDate = deliveryDate, Instructions = instructions, IndentNo = string.IsNullOrWhiteSpace(indentNo) ? null : indentNo.Trim(),
            Movement = movement,
            CrossMode = movement == MovementType.CrossBilling ? crossMode : null, CrossAmount = movement == MovementType.CrossBilling && crossMode == CrossBillingMode.Lumpsum ? crossAmount : null,
            CrossKg = movement == MovementType.CrossBilling && crossMode == CrossBillingMode.PerKg ? crossKg : null, CrossRatePerKg = movement == MovementType.CrossBilling && crossMode == CrossBillingMode.PerKg ? crossRatePerKg : null,
        };
        foreach (var l in lines)
        {
            lr.Lines.Add(LrLine.Create(tenantId, lr.Id, l.Code, l.Name, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, l.VolumeCuFt, l.NetWeightKg, l.InvoiceNo));
        }

        lr.TotalWeightKg = lr.Lines.Sum(l => l.WeightKg);
        lr.TotalVolumeCbm = lr.Lines.All(l => l.VolumeCbm is not null) && lr.Lines.Count > 0 ? lr.Lines.Sum(l => l.VolumeCbm!.Value) : null;
        lr.TotalPackages = lr.Lines.Sum(l => l.Packages);
        return lr;
    }

    public void IssueToken(string hash, string cipher, DateTimeOffset at, DateTimeOffset expiresAt)
    {
        TokenHash = hash;
        TokenCipher = cipher;
        TokenIssuedAt = at;
        TokenExpiresAt = expiresAt;
        TokenRevokedAt = null;
    }

    public void RevokeToken(DateTimeOffset at) => TokenRevokedAt ??= at;

    public void Printed() => PrintCount++;

    public Result AssignTrip(Guid tripId, Guid stopId, string? vehicleRegistration)
    {
        if (Status is not (DeliveryStatus.Created or DeliveryStatus.Assigned))
        {
            return Error.Conflict("ops.lr_state", "An LR that is already moving cannot be moved to another trip.");
        }

        TripId = tripId;
        TripStopId = stopId;
        VehicleRegistration = vehicleRegistration;
        Status = DeliveryStatus.Assigned;
        return Result.Success();
    }

    public Result Unassign()
    {
        if (Status != DeliveryStatus.Assigned)
        {
            return Error.Conflict("ops.lr_state", "Only an LR that has not started loading can be taken off its trip.");
        }

        TripId = null;
        TripStopId = null;
        VehicleRegistration = null;
        Status = DeliveryStatus.Created;
        return Result.Success();
    }

    /// <summary>A CFA event on the LR's trip.</summary>
    public void ApplyCfaEvent(EventType type, DateTimeOffset at)
    {
        switch (type)
        {
            case EventType.CfaReached:
                CfaReachedAt = at;
                Status = DeliveryStatus.AtCfa;
                break;
            case EventType.LoadingStarted:
                LoadingStartedAt = at;
                Status = DeliveryStatus.Loading;
                break;
            case EventType.LoadingCompleted:
                LoadingCompletedAt = at;
                Status = DeliveryStatus.Loaded;
                break;
            case EventType.CfaDeparted:
                DepartedAt = at;
                Status = DeliveryStatus.InTransit;
                break;
        }
    }

    public Result Arrive(DateTimeOffset at)
    {
        if (Status != DeliveryStatus.InTransit)
        {
            return Error.Conflict("ops.arrival_state", "Arrival can only be confirmed while the vehicle is in transit.");
        }

        ArrivedAt = at;
        Status = DeliveryStatus.Arrived;
        return Result.Success();
    }

    public Result CompleteUnloading(DateTimeOffset at)
    {
        if (Status != DeliveryStatus.Arrived)
        {
            return Error.Conflict("ops.unloading_state", ArrivedAt is null ? "Unloading cannot be completed before arrival has been confirmed." : "Unloading has already been completed.");
        }

        UnloadedAt = at;
        Status = DeliveryStatus.UnloadingCompleted;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is DeliveryStatus.InTransit or DeliveryStatus.Arrived or DeliveryStatus.UnloadingCompleted)
        {
            return Error.Conflict("ops.lr_state", "An LR that has left the CFA cannot be cancelled.");
        }

        Status = DeliveryStatus.Cancelled;
        TokenRevokedAt ??= DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>A corrected time for one of the milestones (office only; the reason is kept on the correction event).</summary>
    public void SetTime(EventType type, DateTimeOffset at)
    {
        switch (type)
        {
            case EventType.CfaReached: CfaReachedAt = at; break;
            case EventType.LoadingStarted: LoadingStartedAt = at; break;
            case EventType.LoadingCompleted: LoadingCompletedAt = at; break;
            case EventType.CfaDeparted: DepartedAt = at; break;
            case EventType.DestinationArrival: ArrivedAt = at; break;
            case EventType.UnloadingCompleted: UnloadedAt = at; break;
        }
    }
}

public sealed class TripStop : Entity, ITenantScoped
{
    private TripStop()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TripId { get; private set; }

    public int Sequence { get; private set; }

    public Guid DestinationLocationId { get; private set; }

    public DateTimeOffset? ArrivedAt { get; private set; }

    public DateTimeOffset? UnloadedAt { get; private set; }

    public static TripStop Create(Guid tenantId, Guid tripId, int sequence, Guid destination) => new() { TenantId = tenantId, TripId = tripId, Sequence = sequence, DestinationLocationId = destination };

    public void Arrived(DateTimeOffset at) => ArrivedAt ??= at;

    public void CorrectArrived(DateTimeOffset at) => ArrivedAt = at;

    public void Unloaded(DateTimeOffset at) => UnloadedAt = at;
}

/// <summary>One vehicle journey from a CFA to one or more distributors. Each stop has its own LRs, arrival and unloading.</summary>
public sealed class Trip : AggregateRoot, ITenantScoped
{
    private Trip()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid CfaLocationId { get; private set; }

    /// <summary>The vehicle from the vehicle master when one was chosen; the registration is kept as text so an old trip still reads the same.</summary>
    public Guid? VehicleId { get; private set; }

    public string VehicleRegistration { get; private set; } = null!;

    public Guid? TransporterId { get; private set; }

    public string? DriverName { get; private set; }

    public string? DriverPhone { get; private set; }

    public DateOnly? PlannedDate { get; private set; }

    public TripStatus Status { get; private set; } = TripStatus.Planned;

    public string? Remarks { get; private set; }

    public DateTimeOffset? CfaReachedAt { get; private set; }

    public DateTimeOffset? LoadingStartedAt { get; private set; }

    public DateTimeOffset? LoadingCompletedAt { get; private set; }

    public DateTimeOffset? DepartedAt { get; private set; }

    public List<TripStop> Stops { get; private set; } = [];

    public static Trip Create(Guid tenantId, string number, Guid cfa, Guid? vehicleId, string registration, Guid? transporterId, string? driverName, string? driverPhone, DateOnly? plannedDate, string? remarks) => new()
    {
        TenantId = tenantId, Number = number, CfaLocationId = cfa, VehicleId = vehicleId, VehicleRegistration = registration.Trim().ToUpperInvariant(), TransporterId = transporterId,
        DriverName = Customer.Clean(driverName), DriverPhone = Customer.Clean(driverPhone), PlannedDate = plannedDate, Remarks = Customer.Clean(remarks),
    };

    public bool HasLeft => DepartedAt is not null;

    public TripStop AddStop(Guid destination)
    {
        var stop = TripStop.Create(TenantId, Id, Stops.Count == 0 ? 1 : Stops.Max(s => s.Sequence) + 1, destination);
        Stops.Add(stop);
        return stop;
    }

    public Result Update(string registration, Guid? vehicleId, Guid? transporterId, string? driverName, string? driverPhone, DateOnly? plannedDate, string? remarks)
    {
        if (HasLeft)
        {
            return Error.Conflict("ops.trip_left", "A trip that has left the CFA cannot change its vehicle.");
        }

        VehicleRegistration = registration.Trim().ToUpperInvariant();
        VehicleId = vehicleId;
        TransporterId = transporterId;
        DriverName = Customer.Clean(driverName);
        DriverPhone = Customer.Clean(driverPhone);
        PlannedDate = plannedDate;
        Remarks = Customer.Clean(remarks);
        return Result.Success();
    }

    /// <summary>
    /// Records a CFA event. They happen once and in order: reached, loading started, loading completed, departed; and each time cannot be earlier than the one before it.
    /// </summary>
    public Result RecordCfaEvent(EventType type, DateTimeOffset at)
    {
        if (Status == TripStatus.Cancelled)
        {
            return Error.Conflict("ops.trip_cancelled", "The trip is cancelled.");
        }

        var (existing, previous, previousName) = type switch
        {
            EventType.CfaReached => (CfaReachedAt, (DateTimeOffset?)null, string.Empty),
            EventType.LoadingStarted => (LoadingStartedAt, CfaReachedAt, "the vehicle reached the CFA"),
            EventType.LoadingCompleted => (LoadingCompletedAt, LoadingStartedAt, "loading started"),
            EventType.CfaDeparted => (DepartedAt, LoadingCompletedAt, "loading completed"),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        if (existing is not null)
        {
            return Error.Conflict("ops.event_duplicate", "That event has already been recorded; use a correction to change its time.");
        }

        if (type != EventType.CfaReached && previous is null)
        {
            return Error.Conflict("ops.event_order", $"This cannot be recorded before {previousName}.");
        }

        if (previous is { } p && at < p)
        {
            return Error.Validation("ops.event_time_order", $"This cannot be earlier than {previousName} ({p:dd MMM HH:mm}).");
        }

        Set(type, at);
        Status = type switch
        {
            EventType.CfaReached => TripStatus.AtCfa,
            EventType.LoadingStarted => TripStatus.Loading,
            EventType.LoadingCompleted => TripStatus.Loaded,
            _ => TripStatus.InTransit,
        };
        return Result.Success();
    }

    /// <summary>Changes the time of an event already recorded, keeping the order of the sequence.</summary>
    public Result CorrectCfaTime(EventType type, DateTimeOffset at)
    {
        var (current, before, after) = type switch
        {
            EventType.CfaReached => (CfaReachedAt, (DateTimeOffset?)null, LoadingStartedAt),
            EventType.LoadingStarted => (LoadingStartedAt, CfaReachedAt, LoadingCompletedAt),
            EventType.LoadingCompleted => (LoadingCompletedAt, LoadingStartedAt, DepartedAt),
            EventType.CfaDeparted => (DepartedAt, LoadingCompletedAt, (DateTimeOffset?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        if (current is null)
        {
            return Error.Conflict("ops.event_missing", "That event has not been recorded yet.");
        }

        if ((before is { } b && at < b) || (after is { } a && at > a))
        {
            return Error.Validation("ops.event_time_order", "The corrected time must stay between the events before and after it.");
        }

        Set(type, at);
        return Result.Success();
    }

    public void CompleteIfAllUnloaded()
    {
        if (Stops.Count > 0 && Stops.All(s => s.UnloadedAt is not null))
        {
            Status = TripStatus.Completed;
        }
    }

    public Result Cancel()
    {
        if (HasLeft)
        {
            return Error.Conflict("ops.trip_left", "A trip that has left the CFA cannot be cancelled.");
        }

        Status = TripStatus.Cancelled;
        return Result.Success();
    }

    private void Set(EventType type, DateTimeOffset at)
    {
        switch (type)
        {
            case EventType.CfaReached: CfaReachedAt = at; break;
            case EventType.LoadingStarted: LoadingStartedAt = at; break;
            case EventType.LoadingCompleted: LoadingCompletedAt = at; break;
            case EventType.CfaDeparted: DepartedAt = at; break;
        }
    }
}
