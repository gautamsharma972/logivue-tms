using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.India;

namespace Tms.Modules.Shipments.Integration;

/// <summary>
/// What Reports &amp; Analytics reads from planning and shipments, as flat facts. Read-only; every number is Planning's own (plans, costs, savings),
/// and what Planning does not know (planned pickup time, tender view time) is null, which Reports reads as "not measurable".
/// </summary>
internal sealed class ShipmentsReportingProvider(ShipmentsDbContext db, ITransporterDirectory transporters, IVehicleTypeDirectory vehicleTypes) : IPlanningReportingProvider
{
    private sealed record Drop(string Customer, string City, string State, string? Reference);

    private static string Service(FreightMode mode) => mode == FreightMode.Ftl ? "FTL" : "PTL";

    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    public async Task<IReadOnlyList<ShipmentReportFact>> ShipmentsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var query = db.Shipments.AsNoTracking().Include(s => s.Orders).Where(s => s.PlannedPickupDate >= window.From && s.PlannedPickupDate <= window.To);
        if (window.TransporterId is { } own)
        {
            query = query.Where(s => s.TransporterId == own);
        }

        var shipments = await query.ToListAsync(cancellationToken);
        if (shipments.Count == 0)
        {
            return [];
        }

        var drops = await DropsAsync(shipments, cancellationToken);
        var names = await transporters.GetAsync(shipments.Where(s => s.TransporterId is not null).Select(s => s.TransporterId!.Value).Distinct(), cancellationToken);
        var types = await vehicleTypes.GetAsync(shipments.Where(s => s.VehicleTypeId is not null).Select(s => s.VehicleTypeId!.Value).Distinct(), cancellationToken);
        return shipments.Select(s =>
        {
            var drop = drops.GetValueOrDefault(s.Id) ?? new Drop("Unknown customer", s.OriginCity, s.OriginState, null);
            var type = s.VehicleTypeId is { } t ? types.GetValueOrDefault(t) : null;
            var outbound = s.Orders.Count(o => !o.IsReturn);
            return new ShipmentReportFact(
                s.Number, s.Id, s.Status.ToString(), Service(s.Mode), s.PlannedPickupDate, drop.Customer, s.OriginCity, s.OriginState, drop.City, drop.State, IndiaRegions.OfState(s.OriginState), s.TotalWeightKg, s.TotalVolumeCbm,
                s.DistanceKm, Math.Max(1, outbound), s.Orders.Count + 1, s.TransporterId, s.TransporterId is { } id ? names.GetValueOrDefault(id)?.LegalName : null, s.VehicleRegistration, type?.Name, s.VehiclePayloadKg ?? type?.PayloadKg,
                type?.VolumeCbm, s.DriverName, s.PlannedCost, s.FreightEstimate, s.ContractReference, null, s.DispatchedAt, drop.Reference is null ? null : DateOnly.TryParse(drop.Reference, out var by) ? by : null, s.DeliveredAt,
                s.TenderedAt, s.AcceptedAt, s.DispatchedAt, Math.Max(1, outbound), s.PlanReference);
        }).ToList();
    }

    /// <summary>The customer and destination of each shipment: its last outbound drop.</summary>
    private async Task<Dictionary<Guid, Drop>> DropsAsync(IReadOnlyList<Shipment> shipments, CancellationToken cancellationToken)
    {
        var orderIds = shipments.SelectMany(s => s.Orders.Select(o => o.OrderId)).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var result = new Dictionary<Guid, Drop>();
        foreach (var s in shipments)
        {
            var last = s.Orders.Where(o => !o.IsReturn).OrderByDescending(o => o.DropSequence).FirstOrDefault();
            if (last is not null && orders.TryGetValue(last.OrderId, out var o))
            {
                result[s.Id] = new Drop(o.Drop.Name, o.DropCity, o.DropState, o.DeliverByDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<PlanningRunFact>> RunsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        if (window.TransporterId is not null)
        {
            return [];
        }

        var runs = await db.PlanningRuns.AsNoTracking().Where(r => r.PlanningDate >= window.From && r.PlanningDate <= window.To && r.Status != PlanStatus.Running && r.Status != PlanStatus.Cancelled).ToListAsync(cancellationToken);
        return runs.Select(r =>
        {
            var p = r.Plan.Summary;
            var saving = p.ConsolidationSaving + p.BackhaulSaving;
            return new PlanningRunFact(r.Number, r.Id, r.PlanningDate, r.PlanVersion, r.Status.ToString(), r.OrderIds.Count, p.OrdersPlanned, p.OrdersUnplanned, p.VehiclesUsed, (decimal)(p.TotalDistanceKm ?? 0), p.TotalCost,
                p.AverageWeightUtilisation is { } w ? Math.Round(w * 100m, 1) : null, p.AverageVolumeUtilisation is { } v ? Math.Round(v * 100m, 1) : null, p.TotalCost + saving, p.TotalCost, saving);
        }).ToList();
    }

    public async Task<IReadOnlyList<PlanVehicleFact>> VehiclesAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var runs = await db.PlanningRuns.AsNoTracking().Where(r => r.PlanningDate >= window.From && r.PlanningDate <= window.To && r.Status != PlanStatus.Running && r.Status != PlanStatus.Cancelled).ToListAsync(cancellationToken);
        var result = new List<PlanVehicleFact>();
        foreach (var r in runs)
        {
            var n = 0;
            foreach (var v in r.Plan.Vehicles)
            {
                n++;
                if (window.TransporterId is { } own && v.TransporterId != own)
                {
                    continue;
                }

                var last = v.Orders.Where(o => o.Kind == "Delivery").OrderByDescending(o => o.Sequence).FirstOrDefault();
                var lane = last is null ? v.PickupCity : $"{v.PickupCity} → {last.DropCity}";
                var consolidated = v.Orders.Count(o => o.Kind == "Delivery") > 1 && v.ConsolidationSaving is > 0;
                result.Add(new PlanVehicleFact(
                    r.Number, r.PlanningDate, v.ShipmentNumber ?? $"{r.Number}-{n}", v.ShipmentNumber, v.AssignedVehicle?.Registration, v.VehicleTypeName ?? "Unassigned", v.TransporterId, v.TransporterName, Service(v.Mode), lane,
                    IndiaRegions.OfState(v.PickupState), v.Orders.Count, v.Stops?.Count ?? v.Orders.Count + 1, v.WeightKg, v.VolumeCbm ?? 0m, v.WeightUtilisation is { } w ? Math.Round(w * 100m, 1) : null,
                    v.VolumeUtilisation is { } vu ? Math.Round(vu * 100m, 1) : null, v.EstimatedCost, (decimal)(v.DistanceKm ?? 0), v.LoadedKm is { } l ? (decimal)l : null, v.EmptyKm is { } e ? (decimal)e : null, consolidated,
                    v.SeparateCost, v.AdditionalKm is { } ak ? (decimal)ak : null, null, v.PayloadKg, v.VolumeCapacityCbm));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<UnplannedOrderFact>> UnplannedAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        if (window.TransporterId is not null)
        {
            return [];
        }

        var runs = await db.PlanningRuns.AsNoTracking().Where(r => r.PlanningDate >= window.From && r.PlanningDate <= window.To && r.Status != PlanStatus.Running && r.Status != PlanStatus.Cancelled).ToListAsync(cancellationToken);
        var ids = runs.SelectMany(r => r.Plan.Unplanned.Select(u => u.OrderId)).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        return runs.SelectMany(r => r.Plan.Unplanned.Select(u =>
        {
            orders.TryGetValue(u.OrderId, out var o);
            return new UnplannedOrderFact(r.Number, r.PlanningDate, u.Number, o?.Drop.Name ?? "Unknown customer", o?.PickupCity ?? string.Empty, o?.DropCity ?? string.Empty, o?.WeightKg ?? 0m, o?.VolumeCbm, Category(u.Code), u.Reason,
                u.Suggestions.Count == 0 ? null : string.Join("; ", u.Suggestions));
        })).ToList();
    }

    private static string Category(string code) => code switch
    {
        UnplannedCodes.NoVehicleType or UnplannedCodes.NoAvailableVehicle or UnplannedCodes.TransporterRestricted => "NoVehicle",
        UnplannedCodes.PayloadExceeded => "PayloadExceeded",
        UnplannedCodes.VolumeExceeded => "VolumeExceeded",
        UnplannedCodes.DeadlineImpossible => "SlaImpossible",
        UnplannedCodes.NotCompatible or UnplannedCodes.Incompatible or UnplannedCodes.TooLong => "NoCompatibleVehicle",
        UnplannedCodes.NoRate => "NoRate",
        UnplannedCodes.LockConflict => "LockedConflict",
        _ => "Other",
    };

    public async Task<IReadOnlyList<TenderFact>> TendersAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = new DateTimeOffset(window.From.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var to = new DateTimeOffset(window.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var invitees = await db.TenderInvitees.AsNoTracking().Where(i => i.Status != InviteeStatus.Waiting && i.SentAt != null && i.SentAt >= from && i.SentAt < to).ToListAsync(cancellationToken);
        if (window.TransporterId is { } own)
        {
            invitees = invitees.Where(i => i.TransporterId == own).ToList();
        }

        if (invitees.Count == 0)
        {
            return [];
        }

        var shipmentIds = invitees.Select(i => i.ShipmentId).Distinct().ToList();
        var shipments = await db.Shipments.AsNoTracking().Include(s => s.Orders).Where(s => shipmentIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var drops = await DropsAsync(shipments, cancellationToken);
        var names = await transporters.GetAsync(invitees.Select(i => i.TransporterId).Distinct(), cancellationToken);
        var types = await vehicleTypes.GetAsync(shipments.Where(s => s.VehicleTypeId is not null).Select(s => s.VehicleTypeId!.Value).Distinct(), cancellationToken);
        var byShipment = shipments.ToDictionary(s => s.Id);
        return invitees.Where(i => byShipment.ContainsKey(i.ShipmentId)).Select(i =>
        {
            var s = byShipment[i.ShipmentId];
            var d = drops.GetValueOrDefault(s.Id);
            var outcome = i.Status switch
            {
                InviteeStatus.Accepted => "Accepted",
                InviteeStatus.Rejected => "Rejected",
                InviteeStatus.Expired => "Expired",
                InviteeStatus.Sent or InviteeStatus.Bid => "Pending",
                _ => "Withdrawn",
            };
            return new TenderFact(s.Number, i.TransporterId, names.GetValueOrDefault(i.TransporterId)?.LegalName ?? "Unknown transporter", $"{s.OriginCity} → {d?.City ?? s.OriginCity}",
                s.VehicleTypeId is { } t ? types.GetValueOrDefault(t)?.Name : null, Service(s.Mode), i.SentAt!.Value, null, i.RespondedAt, outcome);
        }).ToList();
    }
}
