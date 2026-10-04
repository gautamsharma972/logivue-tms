using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Integration;

/// <summary>
/// What Transporters needs to measure performance, read straight from shipments so nothing is copied and nothing can drift. A shipment
/// is included when its acceptance, dispatch or delivery falls in the range.
/// </summary>
internal sealed class ShipmentOperationsFeed(ShipmentsDbContext db) : IShipmentOperationsFeed
{
    public Task<IReadOnlyList<ShipmentFact>> ListAsync(Guid transporterId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        LoadAsync(transporterId, from, to, cancellationToken);

    public Task<IReadOnlyList<ShipmentFact>> ListAllAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        LoadAsync(null, from, to, cancellationToken);

    public async Task<ShipmentFact?> GetAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = db.Shipments.AsNoTracking().Include(s => s.Orders).Where(s => s.Id == shipmentId && s.TransporterId != null);
        var built = await BuildAsync(await shipment.ToListAsync(cancellationToken), cancellationToken);
        return built.Count == 0 ? null : built[0];
    }

    private async Task<IReadOnlyList<ShipmentFact>> LoadAsync(Guid? transporterId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var shipments = db.Shipments.AsNoTracking().Include(s => s.Orders)
            .Where(s => s.TransporterId != null
                && ((s.AcceptedAt >= from && s.AcceptedAt < to) || (s.DispatchedAt >= from && s.DispatchedAt < to) || (s.DeliveredAt >= from && s.DeliveredAt < to)));
        if (transporterId is { } id)
        {
            shipments = shipments.Where(s => s.TransporterId == id);
        }

        return await BuildAsync(await shipments.ToListAsync(cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<ShipmentFact>> BuildAsync(List<Shipment> list, CancellationToken cancellationToken)
    {
        if (list.Count == 0)
        {
            return [];
        }

        var shipmentIds = list.Select(s => s.Id).ToList();
        var orderIds = list.SelectMany(s => s.Orders.Select(o => o.OrderId)).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.DropState, o.DropCity, o.DeliverByDate }).ToDictionaryAsync(o => o.Id, cancellationToken);
        var firstProof = (await db.PodDocuments.AsNoTracking().Where(p => shipmentIds.Contains(p.ShipmentId))
                .Select(p => new { p.ShipmentId, p.OrderId, p.CreatedAt }).ToListAsync(cancellationToken))
            .GroupBy(p => (p.ShipmentId, p.OrderId)).ToDictionary(g => g.Key, g => g.Min(p => p.CreatedAt));

        return list.Select(s =>
        {
            var outbound = s.Orders.Where(o => !o.IsReturn).OrderBy(o => o.DropSequence).ToList();
            var last = outbound.LastOrDefault() is { } l && orders.TryGetValue(l.OrderId, out var drop) ? drop : null;
            var deadlines = outbound.Select(o => orders.GetValueOrDefault(o.OrderId)?.DeliverByDate).Where(d => d.HasValue).Select(d => d!.Value).ToList();
            var deliveries = s.Orders.Where(o => o.DeliveredAt is not null).Select(o => new OrderDeliveryFact(
                o.OrderId, o.DeliveredAt!.Value, orders.GetValueOrDefault(o.OrderId)?.DeliverByDate,
                firstProof.TryGetValue((s.Id, o.OrderId), out var at) ? at : null, o.PodStatus.ToString(), o.PodRejectionCount, o.HasException)).ToList();
            return new ShipmentFact(
                s.Id, s.Number, s.TransporterId!.Value, s.Mode, s.VehicleTypeId, s.OriginState, s.OriginCity, last?.DropState, last?.DropCity,
                s.PlannedPickupDate, deadlines.Count == 0 ? null : deadlines.Min(), s.AcceptedAt, s.DispatchedAt, s.DeliveredAt, deliveries);
        }).ToList();
    }
}
