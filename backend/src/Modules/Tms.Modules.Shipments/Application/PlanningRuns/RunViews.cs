using Tms.SharedKernel.Contracts;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Application.PlanningRuns;

public sealed record RunVehicleDto(
    Guid Key, int Number, FreightMode Mode, string? VehicleTypeName, string? TransporterName, string? Vehicle, string? Driver, int Orders, decimal WeightKg,
    decimal EstimatedCost, double? DistanceKm, double? LoadedKm, double? EmptyKm, bool IsLocked, bool SequenceLocked, bool AssignmentLocked);

public sealed record RunStopDto(
    Guid VehicleKey, int VehicleNumber, int Sequence, string Kind, string Label, double? Latitude, double? Longitude,
    DateTimeOffset? PlannedArrival, DateTimeOffset? PlannedDeparture, double? WaitMinutes);

public sealed record ConsolidationGroupDto(
    Guid VehicleKey, IReadOnlyList<string> OrderNumbers, string? VehicleTypeName, decimal Cost, decimal? SeparateCost, decimal? Saving, double? AdditionalKm, double? AdditionalMinutes);

public sealed record ConsolidationPreviewDto(
    IReadOnlyList<ConsolidationGroupDto> Groups, int OrdersConsolidated, int OrdersAlone, decimal TotalSaving, IReadOnlyList<UnplannedOrder> Unplanned);

public sealed record ReturnsPlanDto(
    IReadOnlyList<RunVehicleDto> ReturnTrips, IReadOnlyList<RunVehicleDto> Backhauls, decimal BackhaulSaving, double? EmptyKm, IReadOnlyList<UnplannedOrder> Unplanned);

internal static class RunViews
{
    public static IReadOnlyList<RunVehicleDto> Vehicles(PlanSnapshot plan) => plan.Vehicles.Select((v, i) => new RunVehicleDto(
        v.Key, i + 1, v.Mode, v.VehicleTypeName, v.Transporter?.Name ?? v.TransporterName, v.AssignedVehicle?.Registration, v.AssignedDriver?.Name,
        v.Orders.Count, v.WeightKg, v.EstimatedCost, v.DistanceKm, v.LoadedKm, v.EmptyKm, v.IsLocked, v.SequenceLocked, v.AssignmentLocked)).ToList();

    public static IReadOnlyList<RunStopDto> Stops(PlanSnapshot plan) => plan.Vehicles
        .SelectMany((v, i) => (v.Stops ?? []).Select(s => new RunStopDto(
            v.Key, i + 1, s.Sequence, s.Kind, s.Label, s.Latitude, s.Longitude, s.PlannedArrival, s.PlannedDeparture, s.WaitMinutes)))
        .ToList();

    public static ConsolidationPreviewDto Consolidation(PlanSnapshot plan)
    {
        var groups = plan.Vehicles.Where(v => v.Orders.Count(o => o.Kind == "Delivery") > 1)
            .Select(v => new ConsolidationGroupDto(
                v.Key, v.Orders.Select(o => o.Number).ToList(), v.VehicleTypeName, v.EstimatedCost, v.SeparateCost, v.ConsolidationSaving, v.AdditionalKm, v.AdditionalMinutes))
            .ToList();
        var together = plan.Vehicles.Where(v => v.Orders.Count(o => o.Kind == "Delivery") > 1).Sum(v => v.Orders.Count);
        return new ConsolidationPreviewDto(groups, together, plan.Vehicles.Sum(v => v.Orders.Count) - together, groups.Sum(g => g.Saving ?? 0m), plan.Unplanned);
    }

    public static ReturnsPlanDto Returns(PlanSnapshot plan, IReadOnlySet<Guid> returnOrderIds)
    {
        var all = Vehicles(plan);
        var byKey = plan.Vehicles.ToDictionary(v => v.Key);
        bool Carries(RunVehicleDto d, Func<PlannedOrder, bool> test) => byKey[d.Key].Orders.Any(test);
        var backhauls = all.Where(d => Carries(d, o => o.Kind == "ReturnPickup")).ToList();
        var trips = all.Where(d => !backhauls.Contains(d) && Carries(d, o => returnOrderIds.Contains(o.OrderId))).ToList();
        return new ReturnsPlanDto(trips, backhauls, plan.Summary.BackhaulSaving, plan.Summary.TotalEmptyKm, plan.Unplanned.Where(u => returnOrderIds.Contains(u.OrderId)).ToList());
    }
}

internal sealed class RunViewsHandler(RunLoader loader, ShipmentAccess access, PlanInputBuilder builder, IPlanningOptimizer optimizer)
{
    public async Task<Result<IReadOnlyList<RunVehicleDto>>> VehiclesAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await loader.FindAsync(id, write: false, cancellationToken);
        return run.IsFailure ? run.Error : Result.Success(RunViews.Vehicles(run.Value.Plan));
    }

    public async Task<Result<IReadOnlyList<RunStopDto>>> StopsAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await loader.FindAsync(id, write: false, cancellationToken);
        return run.IsFailure ? run.Error : Result.Success(RunViews.Stops(run.Value.Plan));
    }

    /// <summary>Tries consolidation on these orders without saving: which ones would share a vehicle, and what that saves.</summary>
    public async Task<Result<ConsolidationPreviewDto>> ConsolidationAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var options = (request.Options ?? new PlanOptions()) with { AllowConsolidation = true };
        var input = await builder.BuildAsync(request.PlanningDate, request.OrderIds, options, [], cancellationToken);
        if (input.IsFailure)
        {
            return input.Error;
        }

        var plan = await optimizer.OptimizeAsync(input.Value, cancellationToken);
        return RunViews.Consolidation(plan);
    }

    /// <summary>Plans return (reverse) orders, with any outbound orders given: which return trips are needed and which ride a truck back.</summary>
    public async Task<Result<ReturnsPlanDto>> ReturnsAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var options = (request.Options ?? new PlanOptions()) with { AllowBackhaul = true };
        var input = await builder.BuildAsync(request.PlanningDate, request.OrderIds, options, [], cancellationToken);
        if (input.IsFailure)
        {
            return input.Error;
        }

        var returnIds = input.Value.Orders.Where(o => o.Direction == OrderDirection.Reverse).Select(o => o.Id).ToHashSet();
        if (returnIds.Count == 0)
        {
            return Error.Validation("planning.no_returns", "Choose at least one return (reverse) order.");
        }

        var plan = await optimizer.OptimizeAsync(input.Value, cancellationToken);
        return RunViews.Returns(plan, returnIds);
    }
}
