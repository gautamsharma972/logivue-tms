using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.MilkRuns;

internal static class MilkRunMapping
{
    public static readonly Error NotFound = Error.NotFound("milk_runs.not_found", "Milk run not found.");

    public static async Task<MilkRunDto> ToDtoAsync(this MilkRunTemplate t, ShipmentsDbContext db, CancellationToken cancellationToken)
    {
        var ids = t.Stops.Select(s => s.LocationId).Append(t.DepotLocationId).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        string Name(Guid id) => locations.TryGetValue(id, out var l) ? l.Name : "Unknown location";
        return new MilkRunDto(
            t.Id, t.Code, t.Name, t.DepotLocationId, Name(t.DepotLocationId), t.VehicleTypeId, t.MaxStops, t.MaxDurationMinutes, t.DepartureTime, t.Days, t.IsActive,
            t.StopsInSequence.Select(s => new MilkRunStopDto(
                s.Sequence, s.LocationId, Name(s.LocationId), locations.GetValueOrDefault(s.LocationId)?.City ?? string.Empty, locations.GetValueOrDefault(s.LocationId)?.State ?? string.Empty,
                s.Type, s.ServiceMinutes, s.WindowFrom, s.WindowTo)).ToList(),
            t.Version);
    }
}

internal sealed class ListMilkRunsHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<PagedResult<MilkRunDto>>> HandleAsync(ListMilkRunsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var templates = db.MilkRunTemplates.AsNoTracking().Include(t => t.Stops).AsQueryable();
        if (query.Active is { } active)
        {
            templates = templates.Where(t => t.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            templates = templates.Where(t => t.Code.Contains(term) || t.Name.Contains(term));
        }

        var page = await templates.OrderBy(t => t.Name).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var items = new List<MilkRunDto>();
        foreach (var t in page.Items)
        {
            items.Add(await t.ToDtoAsync(db, cancellationToken));
        }

        return new PagedResult<MilkRunDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetMilkRunHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<MilkRunDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var t = await db.MilkRunTemplates.AsNoTracking().Include(x => x.Stops).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return t is null ? MilkRunMapping.NotFound : await t.ToDtoAsync(db, cancellationToken);
    }
}

internal sealed class SaveMilkRunHandler(ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<MilkRunDto>> CreateAsync(SaveMilkRunRequest r, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var checkedRequest = await CheckReferencesAsync(r, cancellationToken);
        if (checkedRequest is not null)
        {
            return checkedRequest;
        }

        var template = MilkRunTemplate.Create(tenantId, r.Code, r.Name, r.DepotLocationId, r.VehicleTypeId, r.MaxStops, r.MaxDurationMinutes, r.DepartureTime, r.Days, Specs(r));
        if (template.IsFailure)
        {
            return template.Error;
        }

        db.MilkRunTemplates.Add(template.Value);
        return await SaveAsync(template.Value, cancellationToken);
    }

    public async Task<Result<MilkRunDto>> UpdateAsync(Guid id, SaveMilkRunRequest r, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var template = await db.MilkRunTemplates.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return MilkRunMapping.NotFound;
        }

        var checkedRequest = await CheckReferencesAsync(r, cancellationToken);
        if (checkedRequest is not null)
        {
            return checkedRequest;
        }

        // The old stops are replaced wholesale: delete them explicitly so EF does not try to update rows that will not exist.
        db.MilkRunStops.RemoveRange(template.Stops);
        var updated = template.Update(r.Code, r.Name, r.DepotLocationId, r.VehicleTypeId, r.MaxStops, r.MaxDurationMinutes, r.DepartureTime, r.Days, Specs(r), r.IsActive);
        if (updated.IsFailure)
        {
            db.ChangeTracker.Clear();
            return updated.Error;
        }

        if (r.Version is { } version)
        {
            db.Entry(template).Property(t => t.Version).OriginalValue = version;
        }

        foreach (var stop in template.Stops)
        {
            db.Entry(stop).State = EntityState.Added;
        }

        return await SaveAsync(template, cancellationToken);
    }

    private static List<MilkRunStopSpec> Specs(SaveMilkRunRequest r) =>
        r.Stops.Select(s => new MilkRunStopSpec(s.LocationId, s.Type, s.ServiceMinutes, s.WindowFrom, s.WindowTo)).ToList();

    /// <summary>The depot and every stop must be an active location with coordinates; a named vehicle type must exist.</summary>
    private async Task<Error?> CheckReferencesAsync(SaveMilkRunRequest r, CancellationToken cancellationToken)
    {
        var ids = r.Stops.Select(s => s.LocationId).Append(r.DepotLocationId).Distinct().ToList();
        var found = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var problems = new Dictionary<string, string[]>();
        if (!found.TryGetValue(r.DepotLocationId, out var depot) || !depot.IsActive)
        {
            problems["depotLocationId"] = ["Choose an active location as the depot."];
        }

        if (r.Stops.Any(s => !found.TryGetValue(s.LocationId, out var l) || !l.IsActive))
        {
            problems["stops"] = ["Every stop must be an active location."];
        }

        if (r.VehicleTypeId is { } typeId && !(await vehicleTypes.GetAsync([typeId], cancellationToken)).ContainsKey(typeId))
        {
            problems["vehicleTypeId"] = ["That vehicle type does not exist."];
        }

        return problems.Count == 0 ? null : Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = problems };
    }

    private async Task<Result<MilkRunDto>> SaveAsync(MilkRunTemplate template, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return Error.Conflict("milk_runs.code_exists", "A milk run with this code already exists.");
        }

        return await template.ToDtoAsync(db, cancellationToken);
    }
}

internal sealed class PlanMilkRunHandler(ShipmentsDbContext db, ShipmentAccess access, IVehicleTypeDirectory vehicleTypes, MilkRunPlanner planner)
{
    /// <summary>
    /// Plans one day of a milk run from the open orders that touch its stops: a pickup stop collects orders from that location to
    /// the depot, a delivery stop takes orders from the depot to that location. Link orders to locations for them to be found.
    /// </summary>
    public async Task<Result<MilkRunPlan>> HandleAsync(PlanMilkRunRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var template = await db.MilkRunTemplates.AsNoTracking().Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == request.MilkRunId, cancellationToken);
        if (template is null)
        {
            return MilkRunMapping.NotFound;
        }

        if (!template.IsActive)
        {
            return Error.Conflict("milk_runs.inactive", "This milk run is switched off. Activate it to plan it.");
        }

        var stops = template.StopsInSequence;
        var ids = stops.Select(s => s.LocationId).Append(template.DepotLocationId).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        if (ids.Any(i => !locations.TryGetValue(i, out var l) || !l.IsActive))
        {
            return Error.Conflict("milk_runs.location_unavailable", "A location on this milk run has been removed or switched off. Edit the milk run.");
        }

        var depot = locations[template.DepotLocationId];
        var definition = new MilkRunDefinition(
            template.Code, template.Name, depot.Name, depot.City, depot.State, depot.Point, template.VehicleTypeId, template.MaxStops, template.MaxDurationMinutes, template.DepartureTime,
            stops.Select(s =>
            {
                var l = locations[s.LocationId];
                return new MilkRunStopDef(l.Id, l.Name, l.City, l.State, l.Point, s.Type, s.ServiceMinutes, s.WindowFrom, s.WindowTo);
            }).ToList());

        var stopLocationIds = stops.Select(s => s.LocationId).ToList();
        var candidates = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Open && o.ReadyDate <= request.Date
                && ((o.DropLocationId == template.DepotLocationId && o.PickupLocationId != null && stopLocationIds.Contains(o.PickupLocationId.Value))
                    || (o.PickupLocationId == template.DepotLocationId && o.DropLocationId != null && stopLocationIds.Contains(o.DropLocationId.Value))))
            .ToListAsync(cancellationToken);

        var orders = new List<MilkRunOrder>();
        foreach (var o in candidates.OrderBy(o => o.Number, StringComparer.Ordinal))
        {
            var index = stops.ToList().FindIndex(s =>
                (s.Type == MilkRunStopType.Pickup && o.PickupLocationId == s.LocationId && o.DropLocationId == template.DepotLocationId)
                || (s.Type == MilkRunStopType.Delivery && o.DropLocationId == s.LocationId && o.PickupLocationId == template.DepotLocationId));
            if (index >= 0)
            {
                orders.Add(new MilkRunOrder(o.Id, o.Number, index, o.WeightKg, o.VolumeCbm, o.DeliverByDate));
            }
        }

        var plan = await planner.PlanAsync(
            new MilkRunInput(request.Date, definition, orders, await vehicleTypes.ListActiveAsync(cancellationToken), new MilkRunOptions(request.KeepTemplateOrder)),
            cancellationToken);

        var warnings = plan.Warnings.ToList();
        if (!template.Days.Contains(request.Date.DayOfWeek))
        {
            warnings.Insert(0, $"{request.Date:dddd} is not one of this run's scheduled days ({string.Join(", ", template.Days)}). Planned anyway.");
        }

        return plan with { Warnings = warnings };
    }
}

public sealed record CommitMilkRunRequest(Guid MilkRunId, DateOnly Date, bool KeepTemplateOrder = true);

public sealed record CommittedTripDto(int TripNumber, Guid ShipmentId, string ShipmentNumber, int Orders, decimal? PlannedCost, bool IsCollectionRun);

public sealed record CommitMilkRunResultDto(string Code, DateOnly Date, IReadOnlyList<CommittedTripDto> Shipments);

/// <summary>
/// Turns a day's milk-run plan into draft shipments, one per trip, so they can be tendered like any other. A trip that only
/// collects becomes a collection run (many pickups, one drop); a trip that delivers is an ordinary run, with any collections riding
/// back as returns. The plan is re-made at commit time, so only orders that are still open are used.
/// </summary>
internal sealed class CommitMilkRunHandler(
    ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user, ISequenceGenerator sequences, PlanMilkRunHandler planner, TimeProvider clock, ILogger<CommitMilkRunHandler> logger)
{
    public async Task<Result<CommitMilkRunResultDto>> HandleAsync(CommitMilkRunRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var planned = await planner.HandleAsync(new PlanMilkRunRequest(request.MilkRunId, request.Date, request.KeepTemplateOrder), cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error;
        }

        var plan = planned.Value;
        if (plan.Trips.Count == 0)
        {
            return Error.Conflict("milk_runs.nothing_to_commit", "There is nothing to commit: no open orders could be planned for this run on that day.");
        }

        var ids = plan.Trips.SelectMany(t => t.Stops).SelectMany(s => s.Orders).Select(o => o.OrderId).ToList();
        var orders = await db.Orders.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var pickup = request.Date < clock.TodayInIndia() ? clock.TodayInIndia() : request.Date;
        var created = new List<CommittedTripDto>();

        foreach (var trip in plan.Trips)
        {
            if (trip.VehicleTypeId is null)
            {
                return Error.Conflict("milk_runs.no_vehicle", $"Trip {trip.Number} has no vehicle type, so it cannot become a shipment.");
            }

            var stopOrders = trip.Stops.Where(s => s.Kind != "Depot").SelectMany(s => s.Orders.Select(o => (Stop: s, Line: o))).ToList();
            var tripOrders = stopOrders.Select(x => orders[x.Line.OrderId]).ToList();
            var number = $"SH-{await sequences.NextAsync(tenantId, "shipment", cancellationToken):D5}";
            var distance = Math.Round((decimal)trip.DistanceKm, 1);

            Result<Shipment> shipment;
            var collectingOnly = tripOrders.All(o => o.Direction == OrderDirection.Reverse);
            if (collectingOnly)
            {
                // The lane is priced from the farthest pickup, as the plan itself priced it.
                var far = trip.Stops.Where(s => s.Kind == "Pickup").Select(s => orders[s.Orders[0].OrderId]).First();
                shipment = Shipment.CreateCollection(tenantId, number, tripOrders, far.PickupState, far.PickupCity, FreightMode.Ftl, trip.VehicleTypeId, pickup, distance);
            }
            else
            {
                // Outbound first so the depot is the origin; collections on the same trip ride back as returns.
                var ordered = tripOrders.OrderBy(o => o.Direction == OrderDirection.Forward ? 0 : 1).ToList();
                shipment = Shipment.Create(tenantId, number, ordered, FreightMode.Ftl, trip.VehicleTypeId, pickup, distance);
                tripOrders = ordered;
            }

            if (shipment.IsFailure)
            {
                return shipment.Error with { Description = $"Trip {trip.Number}: {shipment.Error.Description}" };
            }

            shipment.Value.RecalculateLoad(tripOrders);
            if (trip.Cost is { } cost)
            {
                shipment.Value.RecordPlan($"{plan.Code} {request.Date:yyyy-MM-dd} T{trip.Number}", cost);
            }

            db.Shipments.Add(shipment.Value);
            created.Add(new CommittedTripDto(trip.Number, shipment.Value.Id, number, tripOrders.Count, trip.Cost, shipment.Value.IsCollectionRun));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Milk run {Code} for {Date} committed: {Shipments} shipment(s)", plan.Code, request.Date, created.Count);
        return new CommitMilkRunResultDto(plan.Code, request.Date, created);
    }
}
