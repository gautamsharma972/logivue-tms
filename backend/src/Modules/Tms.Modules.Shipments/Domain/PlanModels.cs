using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

/// <summary>What the optimizer tries to minimise or maximise when several feasible options exist.</summary>
public enum PlanObjective
{
    MinimizeTotalCost = 1,
    MinimizeVehicles = 2,
    MaximizeUtilisation = 3,
    MinimizeDistance = 4,
    BalanceCostAndUtilisation = 5,
}

/// <summary>
/// How the solver ended. Rule-based construction is reported as <see cref="Feasible"/>; <see cref="Optimized"/> is reserved
/// for a solver that can prove its result is the best found, so a heuristic result is never labelled optimal.
/// </summary>
public enum SolverStatus
{
    Feasible = 1,
    Optimized = 2,
    TimeLimitReached = 3,
    Infeasible = 4,
    Failed = 5,
}

public enum PlanStatus
{
    Completed = 1,
    PartiallyPlanned = 2,
    Infeasible = 3,
    Approved = 4,
    Committed = 5,
    Cancelled = 6,

    /// <summary>Being calculated in the background; there is no plan to review yet.</summary>
    Running = 7,
}

/// <summary>What a lock freezes. A re-plan carries anything locked at any level over unchanged; manual edits honour each level.</summary>
public enum LockKind
{
    /// <summary>The whole vehicle: orders, type, stops and who drives. Nothing on it can be edited.</summary>
    Vehicle = 1,

    /// <summary>The order of the stops. Orders may be added or removed but the existing stops keep their sequence.</summary>
    Sequence = 2,

    /// <summary>The assigned vehicle and driver. The trip keeps them whatever else changes.</summary>
    Assignment = 3,

    /// <summary>One order stays on its vehicle: it cannot be moved or removed.</summary>
    Order = 4,
}

/// <summary>One line of a run's execution log: what the planner was doing and when.</summary>
public sealed record PlanLogEntry(DateTimeOffset At, string Message);

/// <summary>Switches and limits for one planning run. Defaults favour a safe, explainable plan.</summary>
public sealed record PlanOptions(
    PlanObjective Objective = PlanObjective.MinimizeTotalCost,
    bool AllowFtl = true,
    bool AllowPtl = true,
    bool AllowConsolidation = true,
    int MaxStops = 8,
    int TimeBudgetSeconds = 20,
    bool EnforceDeadlines = true,
    int PtlExtraTransitHours = 24,
    int StopServiceMinutes = 30,
    int DepartureHour = 8,
    bool AllowBackhaul = true,
    int BackhaulChargePercent = 50,
    int MaxBackhaulExtraKm = 100,
    bool ReturnsAfterDeliveries = true,
    int MaxPtlWeightKg = 5000,
    bool RequireAvailableVehicle = true,
    bool SeparateHazardous = true);

/// <summary>Machine-readable reasons an order could not be planned (stable codes the UI maps to actions).</summary>
public static class UnplannedCodes
{
    public const string NoVehicleType = "NO_VEHICLE_TYPE";
    public const string PayloadExceeded = "PAYLOAD_EXCEEDED";
    public const string VolumeExceeded = "VOLUME_EXCEEDED";
    public const string NoRate = "NO_VALID_RATE";
    public const string ModeNotAllowed = "MODE_NOT_ALLOWED";
    public const string TimeLimit = "TIME_LIMIT_REACHED";
    public const string DeadlineImpossible = "DEADLINE_IMPOSSIBLE";
    public const string RemovedByPlanner = "REMOVED_BY_PLANNER";
    public const string NotCompatible = "NO_COMPATIBLE_VEHICLE";
    public const string TooLong = "ITEM_TOO_LONG";
    public const string NoAvailableVehicle = "NO_AVAILABLE_VEHICLE";
    public const string Incompatible = "PRODUCTS_INCOMPATIBLE";
    public const string LockConflict = "LOCKED_ASSIGNMENT_CONFLICT";
}

/// <param name="Accepted">False when the type cannot carry the load; <see cref="Reason"/> then says by how much.</param>
public sealed record VehicleTypeEvaluation(
    Guid VehicleTypeId, string Name, int PayloadKg, decimal? VolumeCbm, bool Accepted, string? Reason, string? RejectionCode,
    decimal WeightUtilisation, decimal? VolumeUtilisation, decimal? LengthUtilisation = null);

public sealed record PlanQuoteLine(string Code, string Description, decimal Amount);

public sealed record AssignedTransporter(Guid Id, string Code, string Name, string? ContactPerson, string? Phone, string? Email, string? City);

public sealed record AssignedVehicle(Guid Id, string Registration, string TypeName, int PayloadKg, string Compliance, IReadOnlyList<string> Issues);

public sealed record AssignedDriver(Guid Id, string Name, string Phone, string? LicenseNumber, string Compliance, IReadOnlyList<string> Issues);

/// <summary>One way the group could move, and what it would cost. The chosen one is marked; the others say why not.</summary>
public sealed record PlanAlternative(
    FreightMode Mode, Guid? VehicleTypeId, string? VehicleTypeName, Guid? ContractId, string? ContractReference, Guid? TransporterId,
    string? TransporterName, decimal? Total, IReadOnlyList<PlanQuoteLine> Lines, bool Chosen, string Verdict,
    double? TransitHours = null, bool? MeetsDeadline = null,
    AssignedTransporter? Transporter = null, AssignedVehicle? Vehicle = null, AssignedDriver? Driver = null);

/// <param name="Kind">Pickup, Drop, ReturnPickup (a reverse order collected on the way back) or Return (back at the depot).</param>
public sealed record PlannedStop(
    int Sequence, string Kind, string Label, double? Latitude, double? Longitude, DateTimeOffset? PlannedArrival, DateTimeOffset? PlannedDeparture,
    double? WaitMinutes = null);

/// <param name="Kind">Delivery, or ReturnPickup (then City/State are where it is collected).</param>
public sealed record PlannedOrder(
    Guid OrderId, string Number, int Sequence, string DropCity, string DropState, decimal WeightKg, decimal? VolumeCbm, string Kind = "Delivery",
    string Priority = "Normal", bool IsLocked = false);

public sealed record PlannedVehicle(
    Guid Key,
    string PickupCity,
    string PickupState,
    FreightMode Mode,
    Guid? VehicleTypeId,
    string? VehicleTypeName,
    int? PayloadKg,
    decimal? VolumeCapacityCbm,
    Guid? ContractId,
    string? ContractReference,
    Guid? TransporterId,
    string? TransporterName,
    decimal EstimatedCost,
    IReadOnlyList<PlanQuoteLine> CostLines,
    decimal WeightKg,
    decimal? VolumeCbm,
    decimal? WeightUtilisation,
    decimal? VolumeUtilisation,
    IReadOnlyList<PlannedOrder> Orders,
    IReadOnlyList<PlanAlternative> Alternatives,
    string Reason,
    bool IsLocked,
    decimal? ConsolidationSaving,
    Guid? ShipmentId = null,
    string? ShipmentNumber = null,
    double? DistanceKm = null,
    double? DurationMinutes = null,
    RouteSource? RouteSource = null,
    double? TransitHours = null,
    decimal? CostPerTonneKm = null,
    IReadOnlyList<PlannedStop>? Stops = null,
    DateTimeOffset? PlannedDeparture = null,
    string? SequenceMethod = null,
    double? AdditionalKm = null,
    double? AdditionalMinutes = null,
    decimal? SeparateCost = null,
    decimal? SavingPercent = null,
    decimal? BackhaulSaving = null,
    string? RouteNote = null,
    AssignedTransporter? Transporter = null,
    AssignedVehicle? AssignedVehicle = null,
    AssignedDriver? AssignedDriver = null,
    decimal? LengthUtilisation = null,
    double? LoadedKm = null,
    double? EmptyKm = null,
    bool SequenceLocked = false,
    bool AssignmentLocked = false);

public sealed record UnplannedOrder(Guid OrderId, string Number, string Code, string Reason, IReadOnlyList<string> Suggestions);

public sealed record PlanSummary(
    int OrdersPlanned,
    int OrdersUnplanned,
    int VehiclesUsed,
    decimal TotalCost,
    decimal? AverageWeightUtilisation,
    decimal? AverageVolumeUtilisation,
    decimal ConsolidationSaving,
    int FtlCount,
    int PtlCount,
    double? TotalDistanceKm = null,
    decimal? CostPerTonneKm = null,
    decimal BackhaulSaving = 0,
    int ReturnPickups = 0,
    double? TotalLoadedKm = null,
    double? TotalEmptyKm = null,
    decimal? EmptyKmPercent = null,
    decimal? CostPerTonne = null,
    decimal? CostPerShipment = null);

public sealed record PlanSnapshot(
    SolverStatus SolverStatus,
    string? SolverMessage,
    IReadOnlyList<PlannedVehicle> Vehicles,
    IReadOnlyList<UnplannedOrder> Unplanned,
    PlanSummary Summary)
{
    public static PlanSnapshot Build(SolverStatus status, string? message, IReadOnlyList<PlannedVehicle> vehicles, IReadOnlyList<UnplannedOrder> unplanned)
    {
        var withWeight = vehicles.Where(v => v.WeightUtilisation.HasValue).ToList();
        var withVolume = vehicles.Where(v => v.VolumeUtilisation.HasValue).ToList();
        var summary = new PlanSummary(
            vehicles.Sum(v => v.Orders.Count),
            unplanned.Count,
            vehicles.Count,
            vehicles.Sum(v => v.EstimatedCost),
            withWeight.Count == 0 ? null : Math.Round(withWeight.Average(v => v.WeightUtilisation!.Value), 4),
            withVolume.Count == 0 ? null : Math.Round(withVolume.Average(v => v.VolumeUtilisation!.Value), 4),
            vehicles.Sum(v => v.ConsolidationSaving ?? 0m),
            vehicles.Count(v => v.Mode == FreightMode.Ftl),
            vehicles.Count(v => v.Mode == FreightMode.Ptl),
            vehicles.Any(v => v.DistanceKm.HasValue) ? Math.Round(vehicles.Sum(v => v.DistanceKm ?? 0), 1) : null,
            TonneKmCost(vehicles),
            vehicles.Sum(v => v.BackhaulSaving ?? 0m),
            vehicles.Sum(v => v.Orders.Count(o => o.Kind == "ReturnPickup")),
            vehicles.Any(v => v.LoadedKm.HasValue) ? Math.Round(vehicles.Sum(v => v.LoadedKm ?? 0), 1) : null,
            vehicles.Any(v => v.EmptyKm.HasValue) ? Math.Round(vehicles.Sum(v => v.EmptyKm ?? 0), 1) : null,
            EmptyShare(vehicles),
            PerTonne(vehicles),
            vehicles.Count == 0 ? null : Math.Round(vehicles.Sum(v => v.EstimatedCost) / vehicles.Count, 2));
        return new PlanSnapshot(status, message, vehicles, unplanned, summary);
    }

    private static decimal? EmptyShare(IReadOnlyList<PlannedVehicle> vehicles)
    {
        var loaded = vehicles.Sum(v => v.LoadedKm ?? 0);
        var empty = vehicles.Sum(v => v.EmptyKm ?? 0);
        return loaded + empty > 0 ? Math.Round((decimal)(empty / (loaded + empty)) * 100m, 1) : null;
    }

    private static decimal? PerTonne(IReadOnlyList<PlannedVehicle> vehicles)
    {
        var tonnes = vehicles.Sum(v => v.WeightKg + v.Orders.Where(o => o.Kind == "ReturnPickup").Sum(o => o.WeightKg)) / 1000m;
        return tonnes > 0 ? Math.Round(vehicles.Sum(v => v.EstimatedCost) / tonnes, 2) : null;
    }

    /// <summary>Cost over tonne-kilometres, for the vehicles whose distance is known (loaded km only).</summary>
    private static decimal? TonneKmCost(IReadOnlyList<PlannedVehicle> vehicles)
    {
        var measured = vehicles.Where(v => v.DistanceKm is > 0).ToList();
        var tonneKm = measured.Sum(v => (decimal)v.DistanceKm!.Value * v.WeightKg / 1000m);
        return tonneKm > 0 ? Math.Round(measured.Sum(v => v.EstimatedCost) / tonneKm, 2) : null;
    }
}

public static class PlanLocks
{
    /// <summary>True if anything on the vehicle is locked, at any level.</summary>
    public static bool IsPinned(PlannedVehicle v) => v.IsLocked || v.SequenceLocked || v.AssignmentLocked || v.Orders.Any(o => o.IsLocked);
}
