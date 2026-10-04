using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Application.PlanningRuns;

/// <summary>
/// Applies one manual change to a plan. The vehicles it touches are rebuilt and re-validated by the optimizer exactly as
/// specified (capacity, windows, deadlines, return fit); everything else is carried over unchanged. A change that breaks a rule
/// is refused with the reason and nothing is saved. A valid change becomes a new version, so the plan before it is kept.
/// </summary>
internal sealed class EditRunHandler(
    ShipmentsDbContext db, RunLoader loader, Locations.PlannableFactory plannables, IVehicleTypeDirectory vehicleTypes, IPlanningOptimizer optimizer)
{
    private sealed record Draft(List<Guid> Forward, List<Guid> Returns, Guid? VehicleTypeId, FreightMode? Mode, bool KeepOrder, PlannedVehicle? Origin = null);

    private static Error Invalid(string message) => Error.Conflict("planning.edit_invalid", message);

    public async Task<Result<RunDto>> HandleAsync(Guid id, EditPlanRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var run = found.Value;
        if (run.Status is not (PlanStatus.Completed or PlanStatus.PartiallyPlanned))
        {
            return Error.Conflict("planning.not_editable", "Only a plan under review can be edited.");
        }

        if (!await loader.IsLatestAsync(run, cancellationToken))
        {
            return Error.Conflict("planning.not_latest", "A newer version of this plan exists. Edit the latest version.");
        }

        var plan = run.Plan;
        var ids = plan.Vehicles.SelectMany(v => v.Orders.Select(o => o.OrderId)).Concat(plan.Unplanned.Select(u => u.OrderId)).ToHashSet();
        if (request.OrderId is { } wanted)
        {
            ids.Add(wanted);
        }

        var entities = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken);
        if (request.OrderId is { } requested && entities.All(o => o.Id != requested))
        {
            return ShipmentAccess.OrderNotFound;
        }

        var planList = await plannables.BuildAsync(entities, cancellationToken);
        var byId = planList.ToDictionary(p => p.Id);
        var context = new PlanningInput(run.PlanningDate, planList, await vehicleTypes.ListActiveAsync(cancellationToken), run.Options, [], await plannables.IncompatiblePairsAsync(cancellationToken));

        var drafts = new List<Draft>();
        var replaced = new HashSet<Guid>();
        var removed = new List<PlannableOrder>();
        var added = new List<Guid>();
        string description;

        PlannedVehicle? Vehicle(Guid? key) => key is { } k ? plan.Vehicles.FirstOrDefault(v => v.Key == k) : null;
        PlannedVehicle? Carrying(Guid orderId) => plan.Vehicles.FirstOrDefault(v => v.Orders.Any(o => o.OrderId == orderId));
        static List<Guid> ForwardOf(PlannedVehicle v) => v.Orders.Where(o => o.Kind != "ReturnPickup").OrderBy(o => o.Sequence).Select(o => o.OrderId).ToList();
        static List<Guid> ReturnsOf(PlannedVehicle v) => v.Orders.Where(o => o.Kind == "ReturnPickup").OrderBy(o => o.Sequence).Select(o => o.OrderId).ToList();
        static Draft Same(PlannedVehicle v, List<Guid> forward, List<Guid> returns, bool keepOrder) =>
            new(forward, returns, v.Mode == FreightMode.Ftl ? v.VehicleTypeId : null, v.Mode, keepOrder, v);
        Error? Locked(PlannedVehicle v) => v.IsLocked ? Invalid("That vehicle is locked. Unlock it before changing it.") : null;
        Error? OrderLocked(PlannedVehicle v, Guid orderId) =>
            v.Orders.Any(o => o.OrderId == orderId && o.IsLocked) ? Invalid("That order is locked to its vehicle. Unlock it before moving or removing it.") : null;

        switch (request.Kind)
        {
            case PlanEditKind.ReorderStops:
            {
                var v = Vehicle(request.VehicleKey);
                if (v is null || request.OrderIds is null)
                {
                    return Error.Validation("planning.edit_invalid", "Choose a vehicle and give the new stop order.");
                }

                if (Locked(v) is { } locked)
                {
                    return locked;
                }

                if (v.SequenceLocked)
                {
                    return Invalid("The stop sequence of this vehicle is locked. Unlock it before reordering.");
                }

                var current = ForwardOf(v);
                if (request.OrderIds.Count != current.Count || !request.OrderIds.Order().SequenceEqual(current.Order()))
                {
                    return Invalid("The new order must list each outbound stop of this vehicle exactly once.");
                }

                replaced.Add(v.Key);
                drafts.Add(Same(v, [.. request.OrderIds], ReturnsOf(v), keepOrder: true));
                description = $"Reordered the stops of a {v.VehicleTypeName ?? "part-load"} vehicle";
                break;
            }

            case PlanEditKind.ChangeVehicleType:
            {
                var v = Vehicle(request.VehicleKey);
                if (v is null || request.VehicleTypeId is null)
                {
                    return Error.Validation("planning.edit_invalid", "Choose a vehicle and the vehicle type to use.");
                }

                if (Locked(v) is { } locked)
                {
                    return locked;
                }

                if (v.AssignmentLocked)
                {
                    return Invalid("The vehicle and driver of this trip are locked. Unlock them before changing the vehicle type.");
                }

                replaced.Add(v.Key);
                drafts.Add(new Draft(ForwardOf(v), ReturnsOf(v), request.VehicleTypeId, FreightMode.Ftl, KeepOrder: true, v));
                description = $"Changed the vehicle type of a {v.VehicleTypeName ?? "part-load"} vehicle";
                break;
            }

            case PlanEditKind.RemoveOrder:
            {
                if (request.OrderId is not { } orderId || Carrying(orderId) is not { } source)
                {
                    return Error.Validation("planning.edit_invalid", "Choose an order that is on a vehicle.");
                }

                if (Locked(source) is { } locked)
                {
                    return locked;
                }

                if (OrderLocked(source, orderId) is { } orderLocked)
                {
                    return orderLocked;
                }

                var isReturn = ReturnsOf(source).Contains(orderId);
                var forward = ForwardOf(source).Where(i => i != orderId).ToList();
                var returns = ReturnsOf(source).Where(i => i != orderId).ToList();
                if (forward.Count == 0 && returns.Count > 0)
                {
                    return Invalid("This is the only outbound order and the vehicle still collects returns. Remove the returns first.");
                }

                replaced.Add(source.Key);
                if (forward.Count > 0)
                {
                    drafts.Add(Same(source, forward, returns, keepOrder: true));
                }

                removed.Add(byId[orderId]);
                description = $"Removed {byId[orderId].Number} from the plan{(isReturn ? " (return pickup)" : string.Empty)}";
                break;
            }

            case PlanEditKind.MoveOrder:
            case PlanEditKind.AddOrder:
            {
                if (request.OrderId is not { } orderId)
                {
                    return Error.Validation("planning.edit_invalid", "Choose an order.");
                }

                var order = byId[orderId];
                var source = Carrying(orderId);
                if (request.Kind == PlanEditKind.MoveOrder && source is null)
                {
                    return Error.Validation("planning.edit_invalid", "That order is not on a vehicle. Use Add to put it on one.");
                }

                if (request.Kind == PlanEditKind.AddOrder)
                {
                    if (source is not null)
                    {
                        return Invalid($"{order.Number} is already on a vehicle. Use Move.");
                    }

                    if (!run.OrderIds.Contains(orderId))
                    {
                        if (entities.First(e => e.Id == orderId).Status != OrderStatus.Open)
                        {
                            return Invalid($"{order.Number} is not open, so it cannot be planned.");
                        }

                        added.Add(orderId);
                    }
                }

                if (source is not null)
                {
                    if (Locked(source) is { } locked)
                    {
                        return locked;
                    }

                    if (OrderLocked(source, orderId) is { } orderLocked)
                    {
                        return orderLocked;
                    }

                    if (ReturnsOf(source).Contains(orderId))
                    {
                        return Invalid("A return pickup stays with its truck. Remove it and add it to another vehicle instead.");
                    }

                    var left = ForwardOf(source).Where(i => i != orderId).ToList();
                    if (left.Count == 0 && ReturnsOf(source).Count > 0)
                    {
                        return Invalid("This is the only outbound order and the vehicle still collects returns. Remove the returns first.");
                    }

                    replaced.Add(source.Key);
                    if (left.Count > 0)
                    {
                        drafts.Add(Same(source, left, ReturnsOf(source), keepOrder: true));
                    }
                }

                var target = Vehicle(request.ToVehicleKey);
                if (request.ToVehicleKey is not null && target is null)
                {
                    return Error.NotFound("planning.vehicle_not_found", "The target vehicle is not on this plan.");
                }

                if (target is not null)
                {
                    if (source is not null && target.Key == source.Key)
                    {
                        return Invalid("The order is already on that vehicle.");
                    }

                    if (Locked(target) is { } locked)
                    {
                        return locked;
                    }

                    replaced.Add(target.Key);
                    var isReverse = order.Direction == OrderDirection.Reverse;
                    var forward = ForwardOf(target);
                    var returns = ReturnsOf(target);
                    if (isReverse)
                    {
                        returns.Add(orderId); // a reverse order joins a forward truck as a return pickup
                    }
                    else
                    {
                        forward.Add(orderId);
                    }

                    drafts.Add(Same(target, forward, returns, keepOrder: !isReverse));
                    description = $"{(request.Kind == PlanEditKind.AddOrder ? "Added" : "Moved")} {order.Number} {(isReverse ? "as a return pickup to" : "to")} a {target.VehicleTypeName ?? "part-load"} vehicle";
                }
                else
                {
                    drafts.Add(new Draft([orderId], [], null, null, KeepOrder: false));
                    description = $"{(request.Kind == PlanEditKind.AddOrder ? "Added" : "Moved")} {order.Number} {(request.Kind == PlanEditKind.AddOrder ? "on" : "to")} its own vehicle";
                }

                break;
            }

            default:
                return Error.Validation("planning.edit_invalid", "Unknown change.");
        }

        var rebuilt = new List<PlannedVehicle>();
        var occupied = plan.Vehicles.Where(v => !replaced.Contains(v.Key)).ToList(); // vehicles and drivers already on other trips of the plan
        foreach (var draft in drafts)
        {
            var origin = draft.Origin;
            var built = await optimizer.BuildVehicleAsync(
                new PinnedGroup(
                    draft.Forward.Select(i => byId[i]).ToList(), draft.Returns.Select(i => byId[i]).ToList(), draft.VehicleTypeId, draft.Mode,
                    draft.KeepOrder || origin?.SequenceLocked == true,
                    origin?.AssignmentLocked == true ? origin.AssignedVehicle?.Id : null,
                    origin?.AssignmentLocked == true ? origin.AssignedDriver?.Id : null),
                context with { Locked = occupied }, cancellationToken);
            if (built.IsFailure)
            {
                return built.Error;
            }

            // The rebuilt trip keeps the locks the planner had set on it.
            var vehicle = origin is null
                ? built.Value
                : built.Value with
                {
                    SequenceLocked = origin.SequenceLocked,
                    AssignmentLocked = origin.AssignmentLocked,
                    Orders = built.Value.Orders.Select(o => origin.Orders.Any(x => x.OrderId == o.OrderId && x.IsLocked) ? o with { IsLocked = true } : o).ToList(),
                };
            rebuilt.Add(vehicle);
            occupied.Add(vehicle);
        }

        var vehicles = plan.Vehicles.Where(v => !replaced.Contains(v.Key)).Concat(rebuilt).ToList();
        var nowPlanned = vehicles.SelectMany(v => v.Orders.Select(o => o.OrderId)).ToHashSet();
        var unplanned = plan.Unplanned.Where(u => !nowPlanned.Contains(u.OrderId))
            .Concat(removed.Where(o => !nowPlanned.Contains(o.Id)).Select(o => new UnplannedOrder(
                o.Id, o.Number, UnplannedCodes.RemovedByPlanner, "Taken off the plan by the planner.", ["Add it to a vehicle", "Run planning again"])))
            .ToList();

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? string.Empty : $" — {request.Comment.Trim()}";
        var snapshot = PlanSnapshot.Build(
            vehicles.Count == 0 && unplanned.Count > 0 ? SolverStatus.Infeasible : SolverStatus.Feasible,
            "Manually adjusted by a planner. Every vehicle was re-checked for capacity, windows and deadlines.",
            vehicles, unplanned);

        var next = PlanningRun.Create(
            run.TenantId, run.RunGroupId, run.Number, run.PlanVersion + 1, run.PlanningDate, run.Options, run.OrderIds.Concat(added).ToList(), description + comment, snapshot);
        db.PlanningRuns.Add(next);
        await db.SaveChangesAsync(cancellationToken);
        return next.ToDto(true);
    }
}
