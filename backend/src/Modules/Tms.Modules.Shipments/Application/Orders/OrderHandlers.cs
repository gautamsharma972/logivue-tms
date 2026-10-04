using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Application.Locations;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.Orders;

internal static class OrderMapping
{
    public static OrderDto ToDto(this Order o, string? shipmentNumber) => new(
        o.Id, o.Number, o.Direction, o.Status, o.Reference, PartyDto.From(o.Pickup), PartyDto.From(o.Drop), o.WeightKg, o.VolumeCbm,
        o.Packages, o.Description, o.ReadyDate, o.DeliverByDate, o.Notes, o.ShipmentId, shipmentNumber, o.CancelReason, o.Version, o.PickupLocationId, o.DropLocationId, o.DeliveryWindowFrom, o.DeliveryWindowTo,
        o.Priority, o.ProductCategory, o.Handling, o.IsHazardous, o.IsStackable, o.LongestItemM, o.ReturnType, o.ReturnReason, o.PickupWindowFrom, o.PickupWindowTo);

    public static async Task<OrderDto> ToDtoAsync(this Order o, ShipmentsDbContext db, CancellationToken cancellationToken)
    {
        string? number = null;
        if (o.ShipmentId is { } id)
        {
            number = await db.Shipments.AsNoTracking().Where(s => s.Id == id).Select(s => s.Number).FirstOrDefaultAsync(cancellationToken);
        }

        return o.ToDto(number);
    }
}

internal sealed class ListOrdersHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<PagedResult<OrderDto>>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var orders = db.Orders.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.Direction is { } direction)
        {
            orders = orders.Where(o => o.Direction == direction);
        }

        if (!string.IsNullOrWhiteSpace(query.PickupState))
        {
            var state = Text.Normalise(query.PickupState);
            orders = orders.Where(o => o.PickupState == state);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            orders = orders.Where(o => o.Number.Contains(term) || (o.Reference != null && o.Reference.Contains(term)) || o.DropCity.Contains(term) || o.PickupCity.Contains(term));
        }

        var page = await orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var shipmentIds = page.Items.Where(o => o.ShipmentId != null).Select(o => o.ShipmentId!.Value).Distinct().ToList();
        var numbers = await db.Shipments.AsNoTracking().Where(s => shipmentIds.Contains(s.Id)).Select(s => new { s.Id, s.Number }).ToDictionaryAsync(s => s.Id, s => s.Number, cancellationToken);
        return new PagedResult<OrderDto>(
            page.Items.Select(o => o.ToDto(o.ShipmentId is { } id && numbers.TryGetValue(id, out var n) ? n : null)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetOrderHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<OrderDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? ShipmentAccess.OrderNotFound : await order.ToDtoAsync(db, cancellationToken);
    }
}

internal sealed class CreateOrderHandler(ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user, ISequenceGenerator sequences)
{
    public async Task<Result<OrderDto>> HandleAsync(SaveOrderRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var parties = await OrderLocations.ResolveAsync(db, request, cancellationToken);
        if (parties.IsFailure)
        {
            return parties.Error;
        }

        var number = $"ORD-{await sequences.NextAsync(tenantId, "order", cancellationToken):D5}";
        var order = Order.Create(
            tenantId, number, request.Direction, request.Reference, parties.Value.Pickup, parties.Value.Drop, request.WeightKg,
            request.VolumeCbm, request.Packages, request.Description, request.ReadyDate, request.DeliverByDate, request.Notes);
        if (order.IsFailure)
        {
            return order.Error;
        }

        order.Value.LinkLocations(request.PickupLocationId, request.DropLocationId);
        var window = order.Value.SetDeliveryWindow(request.DeliveryWindowFrom, request.DeliveryWindowTo);
        if (window.IsFailure)
        {
            return window.Error;
        }

        var attributes = order.Value.SetPlanningAttributes(
            request.Priority, request.ProductCategory, request.Handling, request.IsHazardous, request.IsStackable, request.LongestItemM,
            request.ReturnType, request.ReturnReason, request.PickupWindowFrom, request.PickupWindowTo);
        if (attributes.IsFailure)
        {
            return attributes.Error;
        }

        db.Orders.Add(order.Value);
        await db.SaveChangesAsync(cancellationToken);
        return order.Value.ToDto(null);
    }
}

internal sealed class UpdateOrderHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<OrderDto>> HandleAsync(Guid id, SaveOrderRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return ShipmentAccess.OrderNotFound;
        }

        var parties = await OrderLocations.ResolveAsync(db, request, cancellationToken);
        if (parties.IsFailure)
        {
            return parties.Error;
        }

        var updated = order.Update(
            request.Direction, request.Reference, parties.Value.Pickup, parties.Value.Drop, request.WeightKg, request.VolumeCbm,
            request.Packages, request.Description, request.ReadyDate, request.DeliverByDate, request.Notes);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        order.LinkLocations(request.PickupLocationId, request.DropLocationId);
        var window = order.SetDeliveryWindow(request.DeliveryWindowFrom, request.DeliveryWindowTo);
        if (window.IsFailure)
        {
            return window.Error;
        }

        var attributes = order.SetPlanningAttributes(
            request.Priority, request.ProductCategory, request.Handling, request.IsHazardous, request.IsStackable, request.LongestItemM,
            request.ReturnType, request.ReturnReason, request.PickupWindowFrom, request.PickupWindowTo);
        if (attributes.IsFailure)
        {
            return attributes.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return order.ToDto(null);
    }
}

internal sealed class CancelOrderHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<OrderDto>> HandleAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return ShipmentAccess.OrderNotFound;
        }

        var cancelled = order.Cancel(request.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return order.ToDto(null);
    }
}
