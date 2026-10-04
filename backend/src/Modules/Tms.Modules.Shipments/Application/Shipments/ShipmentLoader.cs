using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Application.Shipments;

/// <summary>Finds shipments the caller may see and shapes them for the screen (hiding cost from vendors).</summary>
internal sealed class ShipmentLoader(
    ShipmentsDbContext db, ShipmentAccess access, ITransporterDirectory transporters, IVehicleTypeDirectory vehicleTypes)
{
    /// <summary>A shipment the caller may act on, or NotFound. Vendors asking for someone else's load get NotFound, not Forbidden.</summary>
    public async Task<Result<Shipment>> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var query = db.Shipments.Include(s => s.Orders).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var shipment = await query.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return shipment is not null && access.CanSee(shipment) ? shipment : ShipmentAccess.ShipmentNotFound;
    }

    public async Task<Dictionary<Guid, Order>> LoadOrdersAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        var ids = shipment.Orders.Select(o => o.OrderId).ToList();
        return await db.Orders.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
    }

    public async Task<ShipmentDto> ToDtoAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking().Where(o => shipment.Orders.Select(l => l.OrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var documents = await db.PodDocuments.AsNoTracking().Where(d => d.ShipmentId == shipment.Id).GroupBy(d => d.OrderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var summary = (await ToSummariesAsync([shipment], cancellationToken))[0];
        string? vehicleTypeName = null;
        if (shipment.VehicleTypeId is { } typeId)
        {
            vehicleTypeName = (await vehicleTypes.GetAsync([typeId], cancellationToken)).TryGetValue(typeId, out var t) ? t.Name : null;
        }

        var lines = shipment.Orders.OrderBy(o => o.DropSequence)
            .Where(l => orders.ContainsKey(l.OrderId))
            .Select(l =>
            {
                var o = orders[l.OrderId];
                return new ShipmentOrderDto(o.Id, o.Number, l.DropSequence, l.LrNumber, PartyDto.From(o.Pickup), PartyDto.From(o.Drop), o.WeightKg, o.VolumeCbm, o.Description, l.IsReturn,
                    l.DeliveredAt, l.ReceiverName, l.PackagesShipped ?? o.Packages, l.DeliveredPackages, l.DamagedPackages, l.ShortagePackages, l.DeliveryRemarks, l.PodStatus, l.PodRejectionReason,
                    documents.GetValueOrDefault(o.Id));
            }).ToList();

        var showCost = !access.IsVendor;
        return new ShipmentDto(
            summary, shipment.VehicleTypeId, vehicleTypeName, shipment.DistanceKm, shipment.TotalVolumeCbm, shipment.ContractId, showCost ? shipment.ContractReference : null,
            showCost ? shipment.EstimateLines.Select(l => new QuoteLineDto(l.Code, l.Description, l.Amount)).ToList() : null,
            showCost ? shipment.OverrideReason : null, shipment.TenderedAt, shipment.RejectionCount, showCost ? shipment.LastRejectionReason : null,
            shipment.VehicleId, shipment.DriverId, shipment.DriverName, shipment.DriverPhone, shipment.AcceptedAt, shipment.DispatchedAt,
            shipment.DeliveredAt, shipment.CancelReason, lines, shipment.Version, shipment.PlanReference, showCost ? shipment.PlannedCost : null);
    }

    public async Task<IReadOnlyList<ShipmentSummaryDto>> ToSummariesAsync(IReadOnlyList<Shipment> shipments, CancellationToken cancellationToken)
    {
        var names = await transporters.GetAsync(shipments.Where(s => s.TransporterId != null).Select(s => s.TransporterId!.Value), cancellationToken);
        var ids = shipments.Select(s => s.Id).ToList();

        // The lane's end is the last drop; one query for all shipments on the page.
        var lastDrops = await (
            from l in db.ShipmentOrders.AsNoTracking()
            join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
            where ids.Contains(l.ShipmentId)
            where !l.IsReturn
            select new { l.ShipmentId, l.DropSequence, o.DropCity, o.DropState }).ToListAsync(cancellationToken);
        var ends = lastDrops.GroupBy(x => x.ShipmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.DropSequence).First());

        var showCost = !access.IsVendor;
        return shipments.Select(s =>
        {
            var end = ends.GetValueOrDefault(s.Id);
            var lane = end is null ? $"{s.OriginCity}, {s.OriginState}" : Support.LaneLabel(s.OriginCity, s.OriginState, end.DropCity, end.DropState);
            return new ShipmentSummaryDto(
                s.Id, s.Number, s.Status, s.Mode, lane, s.PlannedPickupDate, s.Orders.Count, s.TotalWeightKg, s.TransporterId,
                s.TransporterId is { } t && names.TryGetValue(t, out var info) ? info.LegalName : null,
                s.VehicleRegistration, s.Utilization, showCost ? s.FreightEstimate : null);
        }).ToList();
    }
}
