using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

public sealed record KpiPlan(DateOnly PlanningDate, PlanStatus Status, PlanSnapshot Plan);

public sealed record DailyKpi(DateOnly Date, int Plans, int OrdersPlanned, int OrdersUnplanned, int Vehicles, decimal Cost, decimal? AverageWeightUtilisation);

public sealed record ReasonCount(string Code, int Orders);

public sealed record TransporterKpi(string Name, int Vehicles, decimal Cost);

public sealed record VehicleTypeKpi(string Name, int Vehicles);

/// <param name="TotalSavings">Consolidation plus return-load savings, each measured against the alternative the plan shows.</param>
public sealed record PlanningKpis(
    int Plans,
    int OrdersTotal,
    int OrdersPlanned,
    int OrdersUnplanned,
    int VehiclesUsed,
    decimal TotalFreightCost,
    decimal ConsolidationSaving,
    decimal BackhaulSaving,
    decimal TotalSavings,
    decimal? AverageWeightUtilisation,
    decimal? AverageVolumeUtilisation,
    double TotalDistanceKm,
    decimal? CostPerTonneKm,
    decimal? AverageStopsPerVehicle,
    decimal FtlPercent,
    decimal PtlPercent,
    decimal ConsolidatedPercent,
    decimal ReturnPickupPercent,
    IReadOnlyList<DailyKpi> Daily,
    IReadOnlyList<ReasonCount> UnplannedReasons,
    IReadOnlyList<TransporterKpi> Transporters,
    IReadOnlyList<VehicleTypeKpi> VehicleTypes,
    double TotalLoadedKm = 0,
    double TotalEmptyKm = 0,
    decimal? EmptyKmPercent = null,
    decimal? CostPerTonne = null,
    decimal? CostPerShipment = null);

/// <summary>
/// Planning KPIs across plans. Every figure comes from the plan snapshots as they were made, so it does not drift when rates or
/// vehicles change. Utilisation is reported as separate weight and volume averages, never merged into one score.
/// </summary>
public static class PlanningKpiCalculator
{
    public static PlanningKpis Calculate(IReadOnlyList<KpiPlan> plans)
    {
        var vehicles = plans.SelectMany(p => p.Plan.Vehicles).ToList();
        var unplanned = plans.SelectMany(p => p.Plan.Unplanned).ToList();
        var planned = vehicles.Sum(v => v.Orders.Count);
        var returns = vehicles.Sum(v => v.Orders.Count(o => o.Kind == "ReturnPickup"));

        var consolidation = vehicles.Sum(v => v.ConsolidationSaving ?? 0m);
        var backhaul = vehicles.Sum(v => v.BackhaulSaving ?? 0m);
        var cost = vehicles.Sum(v => v.EstimatedCost);

        var measured = vehicles.Where(v => v.DistanceKm is > 0).ToList();
        var tonneKm = measured.Sum(v => (decimal)v.DistanceKm!.Value * (v.WeightKg + v.Orders.Where(o => o.Kind == "ReturnPickup").Sum(o => o.WeightKg)) / 1000m);

        var loadedKm = vehicles.Sum(v => v.LoadedKm ?? 0);
        var emptyKm = vehicles.Sum(v => v.EmptyKm ?? 0);
        var tonnes = vehicles.Sum(v => v.WeightKg + v.Orders.Where(o => o.Kind == "ReturnPickup").Sum(o => o.WeightKg)) / 1000m;

        var withWeight = vehicles.Where(v => v.WeightUtilisation.HasValue).ToList();
        var withVolume = vehicles.Where(v => v.VolumeUtilisation.HasValue).ToList();

        return new PlanningKpis(
            plans.Count,
            planned + unplanned.Count,
            planned,
            unplanned.Count,
            vehicles.Count,
            cost,
            consolidation,
            backhaul,
            consolidation + backhaul,
            withWeight.Count == 0 ? null : Math.Round(withWeight.Average(v => v.WeightUtilisation!.Value), 4),
            withVolume.Count == 0 ? null : Math.Round(withVolume.Average(v => v.VolumeUtilisation!.Value), 4),
            Math.Round(measured.Sum(v => v.DistanceKm!.Value), 1),
            tonneKm > 0 ? Math.Round(measured.Sum(v => v.EstimatedCost) / tonneKm, 2) : null,
            vehicles.Count == 0 ? null : Math.Round((decimal)vehicles.Average(v => v.Stops is { } s ? s.Count(x => x.Kind is "Drop" or "ReturnPickup") : v.Orders.Count), 1),
            Percent(vehicles.Count(v => v.Mode == FreightMode.Ftl), vehicles.Count),
            Percent(vehicles.Count(v => v.Mode == FreightMode.Ptl), vehicles.Count),
            Percent(vehicles.Count(v => v.Orders.Count(o => o.Kind == "Delivery") > 1), vehicles.Count),
            Percent(returns, planned),
            plans.GroupBy(p => p.PlanningDate).OrderBy(g => g.Key).Select(g =>
            {
                var v = g.SelectMany(p => p.Plan.Vehicles).ToList();
                var w = v.Where(x => x.WeightUtilisation.HasValue).ToList();
                return new DailyKpi(
                    g.Key, g.Count(), v.Sum(x => x.Orders.Count), g.Sum(p => p.Plan.Unplanned.Count), v.Count, v.Sum(x => x.EstimatedCost),
                    w.Count == 0 ? null : Math.Round(w.Average(x => x.WeightUtilisation!.Value), 4));
            }).ToList(),
            unplanned.GroupBy(u => u.Code).Select(g => new ReasonCount(g.Key, g.Count())).OrderByDescending(r => r.Orders).ThenBy(r => r.Code, StringComparer.Ordinal).ToList(),
            vehicles.GroupBy(v => v.TransporterName ?? "Unassigned").Select(g => new TransporterKpi(g.Key, g.Count(), g.Sum(v => v.EstimatedCost)))
                .OrderByDescending(t => t.Cost).Take(10).ToList(),
            vehicles.GroupBy(v => v.Mode == FreightMode.Ptl ? "Part load" : v.VehicleTypeName ?? "Full truck").Select(g => new VehicleTypeKpi(g.Key, g.Count()))
                .OrderByDescending(t => t.Vehicles).ThenBy(t => t.Name, StringComparer.Ordinal).ToList(),
            Math.Round(loadedKm, 1),
            Math.Round(emptyKm, 1),
            loadedKm + emptyKm > 0 ? Math.Round((decimal)(emptyKm / (loadedKm + emptyKm)) * 100m, 1) : null,
            tonnes > 0 ? Math.Round(cost / tonnes, 2) : null,
            vehicles.Count > 0 ? Math.Round(cost / vehicles.Count, 2) : null);
    }

    private static decimal Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100m / whole, 1);
}
