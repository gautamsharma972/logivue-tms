using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

/// <summary>
/// One version of a plan. Re-planning never edits a version: it adds the next one under the same <see cref="Number"/>, so
/// every earlier result stays available. The plan is a snapshot — rates, vehicle capacities and costs as they were when it
/// was made — so later master-data changes cannot alter history.
/// </summary>
public sealed class PlanningRun : AggregateRoot, ITenantScoped
{
    public Guid TenantId { get; private set; }

    /// <summary>Shared by every version of the same plan.</summary>
    public Guid RunGroupId { get; private set; }

    public string Number { get; private set; } = null!;

    public int PlanVersion { get; private set; }

    public DateOnly PlanningDate { get; private set; }

    public PlanOptions Options { get; private set; } = null!;

    public IReadOnlyList<Guid> OrderIds { get; private set; } = [];

    /// <summary>Why this version exists (blank for the first).</summary>
    public string? Reason { get; private set; }

    public PlanStatus Status { get; private set; }

    public PlanSnapshot Plan { get; private set; } = null!;

    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>What the planner did and when, kept with the plan so a result can be explained later.</summary>
    public IReadOnlyList<PlanLogEntry> Log { get; private set; } = [];

    public static PlanningRun Create(
        Guid tenantId, Guid runGroupId, string number, int planVersion, DateOnly date, PlanOptions options, IReadOnlyList<Guid> orderIds, string? reason, PlanSnapshot plan,
        IReadOnlyList<PlanLogEntry>? log = null, DateTimeOffset? startedAt = null, DateTimeOffset? completedAt = null) =>
        new()
        {
            TenantId = tenantId, RunGroupId = runGroupId, Number = number, PlanVersion = planVersion, PlanningDate = date, Options = options,
            OrderIds = orderIds, Reason = reason, Plan = plan, Status = StatusOf(plan), Log = log ?? [], StartedAt = startedAt, CompletedAt = completedAt,
        };

    /// <summary>A run that is being calculated in the background. It has no plan until <see cref="Complete"/>.</summary>
    public static PlanningRun CreateRunning(
        Guid tenantId, Guid runGroupId, string number, int planVersion, DateOnly date, PlanOptions options, IReadOnlyList<Guid> orderIds, string? reason, DateTimeOffset now) =>
        new()
        {
            TenantId = tenantId, RunGroupId = runGroupId, Number = number, PlanVersion = planVersion, PlanningDate = date, Options = options, OrderIds = orderIds, Reason = reason,
            Plan = PlanSnapshot.Build(SolverStatus.Feasible, "Being calculated.", [], []), Status = PlanStatus.Running, StartedAt = now,
            Log = [new PlanLogEntry(now, "Queued for planning.")],
        };

    public bool IsRunning => Status == PlanStatus.Running;

    public void AddLog(string message, DateTimeOffset now) => Log = [.. Log, new PlanLogEntry(now, message.Length > 500 ? message[..500] : message)];

    /// <summary>Stores the result of a background run. Ignored if the run was cancelled in the meantime.</summary>
    public Result Complete(PlanSnapshot plan, DateTimeOffset now)
    {
        if (!IsRunning)
        {
            return Error.Conflict("planning.not_running", "This run is no longer running.");
        }

        Plan = plan;
        Status = StatusOf(plan);
        CompletedAt = now;
        return Result.Success();
    }

    public Result Fail(string message, DateTimeOffset now)
    {
        if (!IsRunning)
        {
            return Error.Conflict("planning.not_running", "This run is no longer running.");
        }

        Plan = PlanSnapshot.Build(SolverStatus.Failed, message, [], []);
        Status = PlanStatus.Infeasible;
        CompletedAt = now;
        AddLog(message, now);
        return Result.Success();
    }

    public static PlanStatus StatusOf(PlanSnapshot plan) =>
        plan.SolverStatus is SolverStatus.Infeasible or SolverStatus.Failed ? PlanStatus.Infeasible
        : plan.Unplanned.Count > 0 ? PlanStatus.PartiallyPlanned
        : PlanStatus.Completed;

    private bool Reviewable => Status is PlanStatus.Completed or PlanStatus.PartiallyPlanned;

    /// <summary>Lock or unlock a vehicle, its stop sequence, its assigned vehicle and driver, or one of its orders.</summary>
    public Result SetLock(Guid vehicleKey, bool locked, LockKind kind = LockKind.Vehicle, Guid? orderId = null)
    {
        if (!Reviewable)
        {
            return Error.Conflict("planning.not_editable", "Only a plan under review can be changed.");
        }

        if (Plan.Vehicles.All(v => v.Key != vehicleKey))
        {
            return Error.NotFound("planning.vehicle_not_found", "That vehicle is not on this plan.");
        }

        var target = Plan.Vehicles.First(v => v.Key == vehicleKey);
        if (!Enum.IsDefined(kind))
        {
            return Error.Validation("planning.lock_invalid", "Choose what to lock.");
        }

        if (kind == LockKind.Assignment && locked && (target.AssignedVehicle is null || target.AssignedDriver is null))
        {
            return Error.Conflict("planning.lock_invalid", "This trip has no vehicle and driver assigned yet, so there is nothing to lock.");
        }

        if (kind == LockKind.Order && (orderId is null || target.Orders.All(o => o.OrderId != orderId)))
        {
            return Error.NotFound("planning.order_not_found", "That order is not on this vehicle.");
        }

        PlannedVehicle Apply(PlannedVehicle v) => kind switch
        {
            LockKind.Vehicle => v with { IsLocked = locked },
            LockKind.Sequence => v with { SequenceLocked = locked },
            LockKind.Assignment => v with { AssignmentLocked = locked },
            _ => v with { Orders = v.Orders.Select(o => o.OrderId == orderId ? o with { IsLocked = locked } : o).ToList() },
        };

        Plan = Plan with { Vehicles = Plan.Vehicles.Select(v => v.Key == vehicleKey ? Apply(v) : v).ToList() };
        return Result.Success();
    }

    public Result Approve(Guid? userId, DateTimeOffset now)
    {
        if (!Reviewable)
        {
            return Error.Conflict("planning.not_reviewable", "Only a completed plan can be approved.");
        }

        if (Plan.Vehicles.Count == 0)
        {
            return Error.Conflict("planning.nothing_planned", "There is nothing to approve: no order could be planned.");
        }

        Status = PlanStatus.Approved;
        ApprovedAt = now;
        ApprovedBy = userId;
        return Result.Success();
    }

    /// <summary>Records the operational shipments created from this plan. The plan itself is not otherwise touched.</summary>
    public Result Commit(IReadOnlyDictionary<Guid, (Guid ShipmentId, string Number)> shipments, DateTimeOffset now)
    {
        if (Status != PlanStatus.Approved)
        {
            return Error.Conflict("planning.not_approved", "Approve the plan before committing it.");
        }

        Plan = Plan with
        {
            Vehicles = Plan.Vehicles.Select(v => shipments.TryGetValue(v.Key, out var s) ? v with { ShipmentId = s.ShipmentId, ShipmentNumber = s.Number } : v).ToList(),
        };
        Status = PlanStatus.Committed;
        CommittedAt = now;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status is PlanStatus.Committed or PlanStatus.Cancelled)
        {
            return Error.Conflict("planning.not_cancellable", "A committed or cancelled plan cannot be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("planning.reason_required", "Say why the plan is being cancelled.");
        }

        CancelReason = reason.Trim();
        Status = PlanStatus.Cancelled;
        return Result.Success();
    }
}
