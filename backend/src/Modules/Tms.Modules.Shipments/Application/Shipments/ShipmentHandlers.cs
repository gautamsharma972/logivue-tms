using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.Shipments;

internal sealed class ListShipmentsHandler(ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader)
{
    public async Task<Result<PagedResult<ShipmentSummaryDto>>> HandleAsync(ListShipmentsQuery query, CancellationToken cancellationToken)
    {
        var shipments = db.Shipments.AsNoTracking().Include(s => s.Orders).AsQueryable();
        if (access.IsVendor)
        {
            if (access.VendorTransporterId is not { } mine || !access.CanRespond)
            {
                return ShipmentAccess.Forbidden;
            }

            shipments = shipments.Where(s => s.TransporterId == mine && s.Status != ShipmentStatus.Draft);
        }
        else if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }
        else if (query.TransporterId is { } transporterId)
        {
            shipments = shipments.Where(s => s.TransporterId == transporterId);
        }

        if (query.Status is { } status)
        {
            shipments = shipments.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            shipments = shipments.Where(s => s.Number.Contains(term) || (s.VehicleRegistration != null && s.VehicleRegistration.Contains(term)) || s.OriginCity.Contains(term));
        }

        var page = await shipments.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Number).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<ShipmentSummaryDto>(await loader.ToSummariesAsync(page.Items.ToList(), cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetShipmentHandler(ShipmentLoader loader)
{
    public async Task<Result<ShipmentDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var shipment = await loader.FindAsync(id, tracked: false, cancellationToken);
        return shipment.IsFailure ? shipment.Error : await loader.ToDtoAsync(shipment.Value, cancellationToken);
    }
}

internal sealed class CreateShipmentHandler(
    ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user, ISequenceGenerator sequences, ShipmentLoader loader)
{
    public async Task<Result<ShipmentDto>> HandleAsync(CreateShipmentRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var ids = request.OrderIds.Distinct().ToList();
        var orders = await db.Orders.Where(o => ids.Contains(o.Id)).ToListAsync(cancellationToken);
        if (orders.Count != ids.Count)
        {
            return ShipmentAccess.OrderNotFound;
        }

        // Keep the order the planner chose: it becomes the first drop sequence.
        orders = ids.Select(id => orders.First(o => o.Id == id)).ToList();

        var number = $"SH-{await sequences.NextAsync(tenantId, "shipment", cancellationToken):D5}";
        var shipment = Shipment.Create(tenantId, number, orders, request.Mode, request.VehicleTypeId, request.PlannedPickupDate, request.DistanceKm);
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        shipment.Value.RecalculateLoad(orders);
        db.Shipments.Add(shipment.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(shipment.Value, cancellationToken);
    }
}

/// <summary>Changes to a draft shipment: plan, order set and drop sequence.</summary>
internal sealed class EditShipmentHandler(ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader)
{
    public Task<Result<ShipmentDto>> UpdatePlanAsync(Guid id, UpdateShipmentPlanRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, (s, _) => s.UpdatePlan(request.Mode, request.VehicleTypeId, request.PlannedPickupDate, request.DistanceKm), cancellationToken);

    public Task<Result<ShipmentDto>> AddOrdersAsync(Guid id, AddOrdersRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, async (s, ct) =>
        {
            var ids = request.OrderIds.Distinct().ToList();
            var orders = await db.Orders.Where(o => ids.Contains(o.Id)).ToListAsync(ct);
            return orders.Count != ids.Count
                ? ShipmentAccess.OrderNotFound
                : s.AddOrders(ids.Select(i => orders.First(o => o.Id == i)).ToList());
        }, cancellationToken);

    public Task<Result<ShipmentDto>> RemoveOrderAsync(Guid id, Guid orderId, CancellationToken cancellationToken) =>
        EditAsync(id, async (s, ct) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            return order is null ? ShipmentAccess.OrderNotFound : s.RemoveOrder(order);
        }, cancellationToken);

    public Task<Result<ShipmentDto>> SequenceAsync(Guid id, SequenceRequest request, CancellationToken cancellationToken) =>
        EditAsync(id, (s, _) => s.Sequence(request.OrderIds), cancellationToken);

    private Task<Result<ShipmentDto>> EditAsync(Guid id, Func<Shipment, CancellationToken, Result> change, CancellationToken cancellationToken) =>
        EditAsync(id, (s, ct) => Task.FromResult(change(s, ct)), cancellationToken);

    private async Task<Result<ShipmentDto>> EditAsync(Guid id, Func<Shipment, CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(id, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;
        var changed = await change(shipment, cancellationToken);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var onShipment = shipment.Orders.Select(l => l.OrderId).ToList();
        shipment.RecalculateLoad(await db.Orders.Where(o => onShipment.Contains(o.Id)).ToListAsync(cancellationToken));

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(shipment, cancellationToken);
    }
}

/// <summary>Ranks the contracts that can carry a shipment and tenders to the chosen one.</summary>
internal sealed class QuoteShipmentHandler(
    ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, IFreightQuoteService quotes, TimeProvider clock)
{
    public async Task<Result<ShipmentQuotesDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(id, tracked: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var set = await QuoteAsync(found.Value, clock, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var cheapest = set.Value.Quotes.Count == 0 ? (decimal?)null : set.Value.Quotes.Min(q => q.Total);
        return new ShipmentQuotesDto(
            set.Value.Quotes.OrderBy(q => q.Total).ThenBy(q => q.ContractReference, StringComparer.Ordinal).Select(q => new ShipmentQuoteDto(
                q.ContractId, q.ContractReference, q.TransporterId, q.TransporterName, q.Mode, q.Lane, q.Total, q.Total == cheapest,
                q.Lines.Select(l => new QuoteLineDto(l.Code, l.Description, l.Amount)).ToList(), q.Notes)).ToList(),
            set.Value.Message);
    }

    /// <summary>Prices the shipment on its pickup date. The lane runs to the last drop; extra drops are charged per the contract.</summary>
    internal async Task<Result<FreightQuoteSet>> QuoteAsync(Shipment shipment, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var outbound = shipment.Orders.Where(o => !o.IsReturn).ToList();
        var last = outbound.OrderByDescending(o => o.DropSequence).FirstOrDefault();
        if (last is null)
        {
            return Error.Validation("shipments.no_orders", "The shipment has no orders.");
        }

        var drop = await db.Orders.AsNoTracking().Where(o => o.Id == last.OrderId).Select(o => new { o.DropState, o.DropCity }).FirstAsync(cancellationToken);
        var date = shipment.PlannedPickupDate < timeProvider.TodayInIndia() ? timeProvider.TodayInIndia() : shipment.PlannedPickupDate;
        return await quotes.QuoteAsync(
            new FreightQuoteRequest(
                date, shipment.OriginState, shipment.OriginCity, drop.DropState, drop.DropCity, shipment.VehicleTypeId, shipment.Mode,
                shipment.TotalWeightKg, shipment.TotalVolumeCbm, shipment.DistanceKm, outbound.Count),
            cancellationToken);
    }
}

internal sealed class TenderShipmentHandler(ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, QuoteShipmentHandler quoter, TimeProvider clock)
{
    public async Task<Result<ShipmentDto>> HandleAsync(Guid id, TenderRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(id, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;

        // Re-price now rather than trusting what the screen showed: rates or diesel may have moved since.
        var set = await quoter.QuoteAsync(shipment, clock, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var chosen = set.Value.Quotes.FirstOrDefault(q => q.ContractId == request.ContractId);
        if (chosen is null)
        {
            return Error.Validation("shipments.quote_unavailable", set.Value.Message ?? "That contract can no longer price this shipment. Refresh the quotes.");
        }

        var tendered = shipment.Tender(chosen.TransporterId, chosen, set.Value.Quotes.Min(q => q.Total), request.OverrideReason, clock.GetUtcNow());
        if (tendered.IsFailure)
        {
            return tendered.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(shipment, cancellationToken);
    }
}

/// <summary>Lifecycle moves after tendering: withdraw, accept, reject, reassign, dispatch, deliver, cancel.</summary>
internal sealed class ShipmentLifecycleHandler(
    ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, IFleetDirectory fleet, ISequenceGenerator sequences, ICurrentUser user, TimeProvider clock)
{
    public Task<Result<ShipmentDto>> WithdrawAsync(Guid id, CancellationToken ct) =>
        PlannerAsync(id, (s, _) => Task.FromResult(s.Withdraw()), ct);

    public Task<Result<ShipmentDto>> DeliverAsync(Guid id, CancellationToken ct) =>
        PlannerAsync(id, async (s, c) => s.Deliver((await loader.LoadOrdersAsync(s, c)).Values, clock.GetUtcNow()), ct);

    public Task<Result<ShipmentDto>> CancelAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        PlannerAsync(id, async (s, c) => s.Cancel(request.Reason, (await loader.LoadOrdersAsync(s, c)).Values), ct);

    public Task<Result<ShipmentDto>> DispatchAsync(Guid id, CancellationToken ct) =>
        PlannerAsync(id, async (s, c) =>
        {
            if (s.Status != ShipmentStatus.Accepted || user.TenantId is not { } tenantId)
            {
                return s.Dispatch([], [], clock.GetUtcNow()); // reports the right conflict
            }

            var numbers = new List<string>();
            for (var i = 0; i < s.Orders.Count; i++)
            {
                numbers.Add($"LR-{await sequences.NextAsync(tenantId, "lr", c):D6}");
            }

            return s.Dispatch(numbers, (await loader.LoadOrdersAsync(s, c)).Values, clock.GetUtcNow());
        }, ct);

    public Task<Result<ShipmentDto>> AcceptAsync(Guid id, AcceptRequest request, CancellationToken ct) =>
        ResponderAsync(id, async (s, c) =>
        {
            var fleetPick = await ResolveFleetAsync(request, c);
            return fleetPick.IsFailure ? fleetPick.Error : s.Accept(fleetPick.Value.Vehicle, fleetPick.Value.Driver, clock.GetUtcNow());
        }, ct);

    public Task<Result<ShipmentDto>> RejectAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        ResponderAsync(id, (s, _) => Task.FromResult(s.Reject(request.Reason)), ct);

    public Task<Result<ShipmentDto>> ReassignAsync(Guid id, AcceptRequest request, CancellationToken ct) =>
        ResponderAsync(id, async (s, c) =>
        {
            var fleetPick = await ResolveFleetAsync(request, c);
            return fleetPick.IsFailure ? fleetPick.Error : s.Reassign(fleetPick.Value.Vehicle, fleetPick.Value.Driver);
        }, ct);

    private async Task<Result<(FleetVehicle Vehicle, FleetDriver Driver)>> ResolveFleetAsync(AcceptRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await fleet.GetVehicleAsync(request.VehicleId, cancellationToken);
        var driver = await fleet.GetDriverAsync(request.DriverId, cancellationToken);
        return vehicle is null || driver is null
            ? Error.NotFound("shipments.fleet_not_found", "That vehicle or driver was not found.")
            : (vehicle, driver);
    }

    private async Task<Result<ShipmentDto>> PlannerAsync(Guid id, Func<Shipment, CancellationToken, Task<Result>> act, CancellationToken cancellationToken) =>
        access.CanPlan ? await RunAsync(id, act, cancellationToken) : ShipmentAccess.Forbidden;

    private async Task<Result<ShipmentDto>> ResponderAsync(Guid id, Func<Shipment, CancellationToken, Task<Result>> act, CancellationToken cancellationToken) =>
        access.CanRespond ? await RunAsync(id, act, cancellationToken) : ShipmentAccess.Forbidden;

    private async Task<Result<ShipmentDto>> RunAsync(Guid id, Func<Shipment, CancellationToken, Task<Result>> act, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var result = await act(found.Value, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

/// <summary>The vehicles and drivers a transporter could put on a load, with paperwork status, so the screen can explain why one is blocked.</summary>
internal sealed class FleetOptionsHandler(ShipmentAccess access, ShipmentLoader loader, IFleetDirectory fleet)
{
    public async Task<Result<FleetOptionsDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRespond)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(id, tracked: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;
        if (shipment.TransporterId is not { } transporterId)
        {
            return Error.Conflict("shipments.not_tendered", "This shipment has not been tendered to a transporter yet.");
        }

        var vehicles = await fleet.ListVehiclesAsync(transporterId, cancellationToken);
        var drivers = await fleet.ListDriversAsync(transporterId, cancellationToken);
        return new FleetOptionsDto(
            vehicles.Select(v =>
            {
                var issues = v.Issues.ToList();
                var tooSmall = v.PayloadKg > 0 && shipment.TotalWeightKg > v.PayloadKg;
                if (tooSmall)
                {
                    issues.Add($"carries only {v.PayloadKg} kg");
                }

                return new FleetOptionDto(v.Id, $"{v.RegistrationNumber} · {v.VehicleTypeName}", v.IsActive && v.Compliance != FleetCompliance.NonCompliant && !tooSmall, v.Compliance == FleetCompliance.ExpiringSoon, issues);
            }).ToList(),
            drivers.Select(d => new FleetOptionDto(d.Id, d.FullName, d.IsActive && d.Compliance != FleetCompliance.NonCompliant, d.Compliance == FleetCompliance.ExpiringSoon, d.Issues)).ToList());
    }
}

/// <summary>How full the trucks were: average, and the loads that went out under-filled.</summary>
internal sealed class UtilizationHandler(ShipmentsDbContext db, ShipmentAccess access, ITransporterDirectory transporters, TimeProvider clock)
{
    public const decimal UnderUtilisedBelow = 0.5m;

    public async Task<Result<UtilizationDto>> HandleAsync(UtilizationQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var to = query.To ?? clock.TodayInIndia();
        var from = query.From ?? to.AddDays(-30);
        var shipments = db.Shipments.AsNoTracking().Where(s =>
            (s.Status == ShipmentStatus.Dispatched || s.Status == ShipmentStatus.Delivered) && s.VehiclePayloadKg != null
            && s.PlannedPickupDate >= from && s.PlannedPickupDate <= to);
        if (query.TransporterId is { } transporterId)
        {
            shipments = shipments.Where(s => s.TransporterId == transporterId);
        }

        var list = await shipments.OrderByDescending(s => s.PlannedPickupDate).Take(1000).ToListAsync(cancellationToken);
        var names = await transporters.GetAsync(list.Where(s => s.TransporterId != null).Select(s => s.TransporterId!.Value), cancellationToken);
        var rows = list.Select(s => new UtilizationRowDto(
            s.Id, s.Number, s.PlannedPickupDate, s.TransporterId is { } t && names.TryGetValue(t, out var n) ? n.LegalName : "Unknown transporter",
            s.VehicleRegistration, s.TotalWeightKg, s.VehiclePayloadKg, s.Utilization)).ToList();
        var known = rows.Where(r => r.Utilization.HasValue).Select(r => r.Utilization!.Value).ToList();
        return new UtilizationDto(rows.Count, known.Count == 0 ? null : Math.Round(known.Average(), 4), known.Count(u => u < UnderUtilisedBelow), rows);
    }
}
