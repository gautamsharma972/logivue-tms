using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Application.Planning;

internal static class PlanningMapping
{
    public static VehicleOptionDto ToDto(this VehicleOption v) => new(v.VehicleTypeId, v.Name, v.PayloadKg, v.WeightUtilization);
}

/// <summary>For one prospective load: which vehicle fits, and whether full truck or part load is cheaper.</summary>
internal sealed class AdviceHandler(ShipmentAccess access, IVehicleTypeDirectory vehicleTypes, IFreightQuoteService quotes, TimeProvider clock)
{
    public async Task<Result<AdviceDto>> HandleAsync(AdviceRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var sizing = VehicleSizer.Recommend(request.WeightKg, request.VolumeCbm, await vehicleTypes.ListActiveAsync(cancellationToken));
        var date = request.Date ?? clock.TodayInIndia();

        async Task<FreightQuoteSet> QuoteAsync(FreightMode mode, Guid? vehicleTypeId) =>
            await quotes.QuoteAsync(
                new FreightQuoteRequest(date, request.OriginState, request.OriginCity, request.DestinationState, request.DestinationCity,
                    vehicleTypeId, mode, request.WeightKg, request.VolumeCbm, request.DistanceKm),
                cancellationToken);

        // A rate may exist only for a bigger truck than the snuggest fit, so price every vehicle that can carry the load.
        FreightQuoteResult? ftlBest = null;
        VehicleOption? ftlVehicle = null;
        string? ftlMessage = null;
        foreach (var option in sizing.Fitting)
        {
            var set = await QuoteAsync(FreightMode.Ftl, option.VehicleTypeId);
            ftlMessage ??= set.Message;
            var best = set.Quotes.OrderBy(q => q.Total).FirstOrDefault();
            if (best is not null && (ftlBest is null || best.Total < ftlBest.Total))
            {
                ftlBest = best;
                ftlVehicle = option;
            }
        }

        var ptl = await QuoteAsync(FreightMode.Ptl, null);
        var ptlBest = ptl.Quotes.OrderBy(q => q.Total).FirstOrDefault();

        var advice = ModeAdvisor.Recommend(ftlBest?.Total, ptlBest?.Total, ftlVehicle?.WeightUtilization ?? sizing.Recommended?.WeightUtilization);
        var modes = new List<ModeOptionDto>
        {
            new(FreightMode.Ftl, ftlBest is not null, ftlBest?.Total, ftlBest?.TransporterName,
                ftlBest is null ? ftlMessage ?? "No full-truck rate." : $"{ftlVehicle!.Name} · {ftlBest.Lane}"),
            new(FreightMode.Ptl, ptlBest is not null, ptlBest?.Total, ptlBest?.TransporterName, ptlBest is null ? ptl.Message ?? "No part-load rate." : ptlBest.Lane),
        };

        return new AdviceDto(
            sizing.Fitting.Select(v => v.ToDto()).ToList(), sizing.Recommended?.ToDto(), sizing.VehiclesNeeded, sizing.Warning,
            modes, advice.Mode, advice.Reason);
    }
}

/// <summary>Proposes loads from the open orders; the planner reviews and creates shipments from them.</summary>
internal sealed class SuggestLoadsHandler(ShipmentsDbContext db, ShipmentAccess access, IVehicleTypeDirectory vehicleTypes)
{
    private const int MaxOrders = 500;

    public async Task<Result<IReadOnlyList<SuggestedLoadDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var open = await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Open)
            .OrderBy(o => o.ReadyDate).ThenBy(o => o.Number).Take(MaxOrders).ToListAsync(cancellationToken);
        var plannable = open.Select(o => new PlannableOrder(
            o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, o.DeliverByDate)).ToList();

        var numbers = open.ToDictionary(o => o.Id, o => o.Number);
        var loads = ConsolidationPlanner.Suggest(plannable, await vehicleTypes.ListActiveAsync(cancellationToken));
        return loads.Select(l => new SuggestedLoadDto(
            l.OrderIds, l.OrderIds.Select(id => numbers[id]).ToList(), l.PickupCity, l.PickupState, l.Drops, l.TotalWeightKg, l.TotalVolumeCbm,
            l.Vehicle?.ToDto(), l.Utilization, l.Suggested, l.EarliestReady, l.EarliestDeadline, l.BackhaulOrderIds, l.Warning)).ToList();
    }
}

/// <summary>Vehicle types for the planning pickers, so planners need no transporter or contract permissions just to choose a truck.</summary>
internal sealed class ListVehicleTypesHandler(ShipmentAccess access, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<IReadOnlyList<VehicleTypeInfo>>> HandleAsync(CancellationToken cancellationToken) =>
        access.CanPlan ? Result.Success(await vehicleTypes.ListActiveAsync(cancellationToken)) : ShipmentAccess.Forbidden;
}
