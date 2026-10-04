using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.PlanningRuns;

internal static class RunMapping
{
    public static RunDto ToDto(this PlanningRun r, bool isLatest) => new(
        r.Id, r.RunGroupId, r.Number, r.PlanVersion, isLatest, r.PlanningDate, r.Status, r.Options, r.OrderIds, r.Reason, r.Plan, r.CreatedAt,
        r.ApprovedAt, r.CommittedAt, r.CancelReason, r.Version, r.StartedAt, r.CompletedAt, r.Log);
}

/// <summary>Loads and validates the orders a run covers and assembles the optimizer's input.</summary>
internal sealed class PlanInputBuilder(ShipmentsDbContext db, IVehicleTypeDirectory vehicleTypes, Locations.PlannableFactory plannables)
{
    public async Task<Result<PlanningInput>> BuildAsync(
        DateOnly date, IReadOnlyList<Guid> orderIds, PlanOptions options, IReadOnlyList<PlannedVehicle> locked, CancellationToken cancellationToken)
    {
        if (OptionRules.Check(options) is { } problem)
        {
            return Error.Validation("planning.options_invalid", problem);
        }

        var lockedIds = locked.SelectMany(v => v.Orders.Select(o => o.OrderId)).ToHashSet();
        var ids = orderIds.Distinct().Where(id => !lockedIds.Contains(id)).ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken);
        if (orders.Count != ids.Count)
        {
            return ShipmentAccess.OrderNotFound;
        }

        var notOpen = orders.Where(o => o.Status != OrderStatus.Open).Select(o => o.Number).Order(StringComparer.Ordinal).ToList();
        if (notOpen.Count > 0)
        {
            return Error.Conflict("planning.orders_not_open", $"These orders are no longer open: {string.Join(", ", notOpen)}.");
        }

        var types = await vehicleTypes.ListActiveAsync(cancellationToken);
        return new PlanningInput(date, await plannables.BuildAsync(orders, cancellationToken), types, options, locked, await plannables.IncompatiblePairsAsync(cancellationToken));
    }
}

internal sealed class PreviewRunHandler(ShipmentAccess access, PlanInputBuilder builder, IPlanningOptimizer optimizer)
{
    /// <summary>Runs the optimizer without saving anything, so a planner can try options freely.</summary>
    public async Task<Result<PlanSnapshot>> HandleAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var input = await builder.BuildAsync(request.PlanningDate, request.OrderIds, request.Options ?? new PlanOptions(), [], cancellationToken);
        return input.IsFailure ? input.Error : await optimizer.OptimizeAsync(input.Value, cancellationToken);
    }
}

internal sealed class CreateRunHandler(
    ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user, ISequenceGenerator sequences, PlanInputBuilder builder, IPlanningOptimizer optimizer,
    IPlanningJobQueue queue, TimeProvider clock, ILogger<CreateRunHandler> logger)
{
    public async Task<Result<RunDto>> HandleAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var options = request.Options ?? new PlanOptions();
        string? reason = null;
        if (request.ModeChoice is { } choice)
        {
            // The planner decided on the full-truck / part-load comparison: plan with that mode only, and keep the decision with the plan.
            options = options with { AllowFtl = choice.Mode == FreightMode.Ftl, AllowPtl = choice.Mode == FreightMode.Ptl };
            var modeName = choice.Mode == FreightMode.Ftl ? "full truck" : "part load";
            reason = choice.Override
                ? $"Recommendation overridden: {modeName} chosen. {choice.Reason?.Trim()}"
                : $"Recommendation accepted: {modeName}.";
        }

        var input = await builder.BuildAsync(request.PlanningDate, request.OrderIds, options, [], cancellationToken);
        if (input.IsFailure)
        {
            return input.Error;
        }

        var number = $"PLN-{request.PlanningDate:yyyyMMdd}-{await sequences.NextAsync(tenantId, $"plan-{request.PlanningDate:yyyyMMdd}", cancellationToken):D3}";
        var orderIds = request.OrderIds.Distinct().ToList();
        if (request.Background)
        {
            // Answer at once; the worker calculates the plan and the caller follows its progress.
            var running = PlanningRun.CreateRunning(tenantId, Guid.CreateVersion7(), number, 1, request.PlanningDate, options, orderIds, reason, clock.GetUtcNow());
            db.PlanningRuns.Add(running);
            await db.SaveChangesAsync(cancellationToken);
            await queue.EnqueueAsync(new PlanningJob(tenantId, user.UserId, running.Id), cancellationToken);
            logger.LogInformation("Planning run {Run} queued: {Orders} order(s)", number, orderIds.Count);
            return running.ToDto(true);
        }

        var started = clock.GetUtcNow();
        var log = new List<PlanLogEntry>();
        Task Note(string message)
        {
            log.Add(new PlanLogEntry(clock.GetUtcNow(), message));
            return Task.CompletedTask;
        }

        var plan = await optimizer.OptimizeAsync(input.Value with { Progress = Note }, cancellationToken);
        var run = PlanningRun.Create(tenantId, Guid.CreateVersion7(), number, 1, request.PlanningDate, options, orderIds, reason, plan, log, started, clock.GetUtcNow());
        db.PlanningRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Planning run {Run} completed as {Status}: {Orders} order(s), {Vehicles} vehicle(s), {Unplanned} unplanned, {Elapsed:0} ms",
            number, run.Status, orderIds.Count, plan.Vehicles.Count, plan.Unplanned.Count, (clock.GetUtcNow() - started).TotalMilliseconds);
        return run.ToDto(true);
    }
}

/// <summary>Finds a run the caller may work on. Planning data is staff-only.</summary>
internal sealed class RunLoader(ShipmentsDbContext db, ShipmentAccess access)
{
    public static readonly Error NotFound = Error.NotFound("planning.not_found", "Planning run not found.");

    public async Task<Result<PlanningRun>> FindAsync(Guid id, bool write, CancellationToken cancellationToken)
    {
        if (write ? !access.CanPlan : !access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        return await db.PlanningRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) is { } run ? run : NotFound;
    }

    public async Task<bool> IsLatestAsync(PlanningRun run, CancellationToken cancellationToken) =>
        !await db.PlanningRuns.AnyAsync(r => r.RunGroupId == run.RunGroupId && r.PlanVersion > run.PlanVersion, cancellationToken);
}

internal sealed class GetRunHandler(RunLoader loader)
{
    public async Task<Result<RunDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await loader.FindAsync(id, write: false, cancellationToken);
        return run.IsFailure ? run.Error : run.Value.ToDto(await loader.IsLatestAsync(run.Value, cancellationToken));
    }
}

internal sealed class ListRunsHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    /// <summary>The newest version of each plan.</summary>
    public async Task<Result<PagedResult<RunSummaryDto>>> HandleAsync(ListRunsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var runs = db.PlanningRuns.AsNoTracking().Where(r => !db.PlanningRuns.Any(n => n.RunGroupId == r.RunGroupId && n.PlanVersion > r.PlanVersion));
        if (query.Status is { } status)
        {
            runs = runs.Where(r => r.Status == status);
        }

        var page = await runs.OrderByDescending(r => r.CreatedAt).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<RunSummaryDto>(
            page.Items.Select(r => new RunSummaryDto(r.Id, r.Number, r.PlanVersion, r.PlanningDate, r.Status, r.Plan.SolverStatus, r.Plan.Summary, r.CreatedAt)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class RunVersionsHandler(ShipmentsDbContext db, RunLoader loader)
{
    public async Task<Result<IReadOnlyList<VersionDto>>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await loader.FindAsync(id, write: false, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var versions = await db.PlanningRuns.AsNoTracking().Where(r => r.RunGroupId == run.Value.RunGroupId).OrderByDescending(r => r.PlanVersion).ToListAsync(cancellationToken);
        return versions.Select(r => new VersionDto(r.Id, r.PlanVersion, r.Status, r.Reason, r.CreatedAt, r.Plan.Summary)).ToList();
    }
}

internal sealed class ReoptimizeRunHandler(ShipmentsDbContext db, RunLoader loader, PlanInputBuilder builder, IPlanningOptimizer optimizer, TimeProvider clock)
{
    /// <summary>Plans again as a new version. Locked vehicles are kept exactly; everything else is re-solved.</summary>
    public async Task<Result<RunDto>> HandleAsync(Guid id, ReoptimizeRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var source = found.Value;
        if (source.Status is PlanStatus.Approved or PlanStatus.Committed or PlanStatus.Cancelled or PlanStatus.Running)
        {
            return Error.Conflict("planning.not_editable", "An approved, committed, cancelled or still-running plan cannot be re-planned.");
        }

        if (!await loader.IsLatestAsync(source, cancellationToken))
        {
            return Error.Conflict("planning.not_latest", "A newer version of this plan exists. Re-plan from the latest version.");
        }

        var options = request.Options ?? source.Options;
        var locked = source.Plan.Vehicles.Where(PlanLocks.IsPinned).ToList();
        var input = await builder.BuildAsync(source.PlanningDate, source.OrderIds, options, locked, cancellationToken);
        if (input.IsFailure)
        {
            return input.Error;
        }

        var started = clock.GetUtcNow();
        var log = new List<PlanLogEntry>();
        Task Note(string message)
        {
            log.Add(new PlanLogEntry(clock.GetUtcNow(), message));
            return Task.CompletedTask;
        }

        var plan = await optimizer.OptimizeAsync(input.Value with { Progress = Note }, cancellationToken);
        var next = PlanningRun.Create(source.TenantId, source.RunGroupId, source.Number, source.PlanVersion + 1, source.PlanningDate, options, source.OrderIds, request.Reason.Trim(), plan, log, started, clock.GetUtcNow());
        db.PlanningRuns.Add(next);
        await db.SaveChangesAsync(cancellationToken);
        return next.ToDto(true);
    }
}

internal sealed class LockVehicleHandler(ShipmentsDbContext db, RunLoader loader)
{
    public async Task<Result<RunDto>> HandleAsync(Guid id, LockRequest request, CancellationToken cancellationToken)
    {
        var run = await loader.FindAsync(id, write: true, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        var locked = run.Value.SetLock(request.VehicleKey, request.Locked, request.Kind, request.OrderId);
        if (locked.IsFailure)
        {
            return locked.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return run.Value.ToDto(await loader.IsLatestAsync(run.Value, cancellationToken));
    }
}

internal sealed class RunLifecycleHandler(
    ShipmentsDbContext db, ShipmentAccess access, RunLoader loader, ICurrentUser user, ISequenceGenerator sequences, TimeProvider clock)
{
    public async Task<Result<RunDto>> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanApprove)
        {
            return ShipmentAccess.Forbidden;
        }

        var run = await loader.FindAsync(id, write: false, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error;
        }

        if (!await loader.IsLatestAsync(run.Value, cancellationToken))
        {
            return Error.Conflict("planning.not_latest", "Only the latest version of a plan can be approved.");
        }

        return await SaveAsync(run.Value, run.Value.Approve(user.UserId, clock.GetUtcNow()), cancellationToken);
    }

    public async Task<Result<RunDto>> CancelAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken)
    {
        var run = await loader.FindAsync(id, write: true, cancellationToken);
        return run.IsFailure ? run.Error : await SaveAsync(run.Value, run.Value.Cancel(request.Reason), cancellationToken);
    }

    /// <summary>Turns each planned vehicle into a draft shipment. Atomic: if any order was taken meanwhile, nothing is created.</summary>
    public async Task<Result<RunDto>> CommitAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanApprove || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(id, write: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var run = found.Value;
        if (run.Status != PlanStatus.Approved)
        {
            return Error.Conflict("planning.not_approved", "Approve the plan before committing it.");
        }

        var ids = run.Plan.Vehicles.SelectMany(v => v.Orders.Select(o => o.OrderId)).ToList();
        var orders = await db.Orders.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var taken = ids.Where(i => !orders.TryGetValue(i, out var o) || o.Status != OrderStatus.Open).Select(i => orders.TryGetValue(i, out var o) ? o.Number : i.ToString()).ToList();
        if (taken.Count > 0)
        {
            return Error.Conflict("planning.orders_not_open", $"These orders are no longer open, so the plan cannot be committed: {string.Join(", ", taken)}. Re-plan.");
        }

        var pickup = run.PlanningDate < clock.TodayInIndia() ? clock.TodayInIndia() : run.PlanningDate;
        var created = new Dictionary<Guid, (Guid, string)>();
        foreach (var vehicle in run.Plan.Vehicles.Where(v => v.ShipmentId is null))
        {
            var vehicleOrders = vehicle.Orders.OrderBy(o => o.Sequence).Select(o => orders[o.OrderId]).ToList();
            var number = $"SH-{await sequences.NextAsync(tenantId, "shipment", cancellationToken):D5}";
            var shipment = Shipment.Create(tenantId, number, vehicleOrders, vehicle.Mode, vehicle.VehicleTypeId, pickup, vehicle.DistanceKm is { } km ? Math.Round((decimal)km, 1) : null);
            if (shipment.IsFailure)
            {
                return shipment.Error;
            }

            shipment.Value.RecalculateLoad(vehicleOrders);
            shipment.Value.RecordPlan($"{run.Number} v{run.PlanVersion}", vehicle.EstimatedCost);
            db.Shipments.Add(shipment.Value);
            created[vehicle.Key] = (shipment.Value.Id, number);
        }

        return await SaveAsync(run, run.Commit(created, clock.GetUtcNow()), cancellationToken);
    }

    private async Task<Result<RunDto>> SaveAsync(PlanningRun run, Result changed, CancellationToken cancellationToken)
    {
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return run.ToDto(await loader.IsLatestAsync(run, cancellationToken));
    }
}

internal sealed class CompareModesHandler(ShipmentsDbContext db, ShipmentAccess access, IVehicleTypeDirectory vehicleTypes, IPlanningOptimizer optimizer, Locations.PlannableFactory plannables, TimeProvider clock)
{
    /// <summary>FTL vs PTL (and each vehicle type) for a set of orders that would travel together.</summary>
    public async Task<Result<ComparisonDto>> HandleAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var ids = request.OrderIds.Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken);
        if (orders.Count != ids.Count)
        {
            return ShipmentAccess.OrderNotFound;
        }

        if (orders.Select(o => (o.PickupState, o.PickupCity)).Distinct().Count() > 1)
        {
            return Error.Validation("planning.pickup_mismatch", "Orders compared together must share one pickup.");
        }

        var options = request.Options ?? new PlanOptions();
        var comparison = await optimizer.CompareAsync(
            await plannables.BuildAsync(orders, cancellationToken), request.Date ?? clock.TodayInIndia(), await vehicleTypes.ListActiveAsync(cancellationToken), options, cancellationToken);

        var priced = comparison.Alternatives.Where(a => a.Total.HasValue).ToList();
        var ftl = priced.Where(a => a.Mode == FreightMode.Ftl).Select(a => a.Total).Min();
        var ptl = priced.Where(a => a.Mode == FreightMode.Ptl).Select(a => a.Total).Min();
        var saving = ftl.HasValue && ptl.HasValue ? Math.Abs(ftl.Value - ptl.Value) : (decimal?)null;
        return new ComparisonDto(comparison.Alternatives, comparison.Chosen, comparison.Reason, comparison.Sizing, ftl, ptl, saving, comparison.Chosen?.Mode);
    }
}

internal sealed class VehicleRecommendationHandler(ShipmentAccess access, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<IReadOnlyList<VehicleTypeEvaluation>>> HandleAsync(RecommendationQuery query, CancellationToken cancellationToken) =>
        !access.CanPlan
            ? ShipmentAccess.Forbidden
            : query.WeightKg <= 0
                ? Error.Validation("planning.weight_required", "Enter a weight greater than zero.")
                : Result.Success(VehicleEvaluator.Evaluate(query.WeightKg, query.VolumeCbm, await vehicleTypes.ListActiveAsync(cancellationToken)));
}
