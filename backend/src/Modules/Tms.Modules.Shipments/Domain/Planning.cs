using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

public sealed record VehicleOption(Guid VehicleTypeId, string Name, int PayloadKg, decimal? VolumeCbm, decimal WeightUtilization, decimal? VolumeUtilization);

/// <param name="Recommended">The smallest vehicle that carries the whole load; null if none does.</param>
/// <param name="VehiclesNeeded">When no single vehicle fits: how many of the largest type are needed.</param>
public sealed record SizingResult(IReadOnlyList<VehicleOption> Fitting, VehicleOption? Recommended, int? VehiclesNeeded, string? Warning);

/// <summary>Recommends a vehicle size from the load's weight and volume.</summary>
public static class VehicleSizer
{
    public static SizingResult Recommend(decimal weightKg, decimal? volumeCbm, IEnumerable<VehicleTypeInfo> types)
    {
        var active = types.Where(t => t.IsActive && t.PayloadKg > 0).OrderBy(t => t.PayloadKg).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        if (active.Count == 0)
        {
            return new SizingResult([], null, null, "No vehicle types are set up.");
        }

        VehicleOption Option(VehicleTypeInfo t) => new(
            t.Id, t.Name, t.PayloadKg, t.VolumeCbm,
            Math.Round(weightKg / t.PayloadKg, 4),
            volumeCbm is { } v && t.VolumeCbm is { } cap and > 0 ? Math.Round(v / cap, 4) : null);

        // A type fits when it carries the weight and, where both sides know a volume, the volume too.
        var fitting = active
            .Where(t => weightKg <= t.PayloadKg && (volumeCbm is not { } v || t.VolumeCbm is not { } cap || v <= cap))
            .Select(Option)
            .ToList();

        if (fitting.Count > 0)
        {
            return new SizingResult(fitting, fitting[0], null, null);
        }

        var largest = active[^1];
        var byWeight = (int)Math.Ceiling(weightKg / largest.PayloadKg);
        var byVolume = volumeCbm is { } vol && largest.VolumeCbm is { } lcap and > 0 ? (int)Math.Ceiling(vol / lcap) : 1;
        var needed = Math.Max(byWeight, byVolume);
        return new SizingResult([], null, needed,
            $"No single vehicle carries {weightKg:0.##} kg{(volumeCbm is null ? string.Empty : $" / {volumeCbm:0.##} CBM")}; it needs {needed} × {largest.Name} (split it across shipments).");
    }
}

public sealed record ModeOption(FreightMode Mode, bool Feasible, decimal? Total, string? Transporter, string Detail);

public sealed record ModeRecommendation(FreightMode? Mode, string Reason);

/// <summary>Chooses between full and part load from what each would cost and how full the truck would be.</summary>
public static class ModeAdvisor
{
    /// <summary>Below this fill level a dedicated truck is rarely worth it unless it is genuinely cheaper.</summary>
    public const decimal LowUtilization = 0.5m;

    public static ModeRecommendation Recommend(decimal? ftlTotal, decimal? ptlTotal, decimal? ftlUtilization)
    {
        switch (ftlTotal, ptlTotal)
        {
            case (null, null):
                return new ModeRecommendation(null, "No contract has a rate for this load either as a full truck or as part load. Add rates or tender manually.");
            case (null, { } ptl):
                return new ModeRecommendation(FreightMode.Ptl, $"Only part-load rates exist for this load (₹{ptl:0.00}).");
            case ({ } ftl, null):
                return new ModeRecommendation(FreightMode.Ftl, $"Only full-truck rates exist for this load (₹{ftl:0.00}).");
        }

        var ftlCost = ftlTotal!.Value;
        var ptlCost = ptlTotal!.Value;
        var fill = ftlUtilization is { } u ? $"{u:P0}" : "an unknown share";

        if (ftlCost < ptlCost)
        {
            return new ModeRecommendation(FreightMode.Ftl, $"A full truck (₹{ftlCost:0.00}, {fill} full) is cheaper than part load (₹{ptlCost:0.00}).");
        }

        if (ptlCost < ftlCost)
        {
            return new ModeRecommendation(FreightMode.Ptl, $"Part load (₹{ptlCost:0.00}) is cheaper than a full truck (₹{ftlCost:0.00}, only {fill} full).");
        }

        // Same price: prefer the full truck when it is well used (faster, single handling), otherwise part load.
        return ftlUtilization is >= LowUtilization
            ? new ModeRecommendation(FreightMode.Ftl, $"Both cost ₹{ftlCost:0.00}; the truck would be {fill} full, so a full truck is simpler.")
            : new ModeRecommendation(FreightMode.Ptl, $"Both cost ₹{ftlCost:0.00}; the truck would only be {fill} full, so part load avoids paying for empty space.");
    }
}

/// <summary>What the consolidation planner needs to know about an open order.</summary>
public sealed record PlannableOrder(
    Guid Id, string Number, OrderDirection Direction, string PickupState, string PickupCity, string DropState, string DropCity,
    decimal WeightKg, decimal? VolumeCbm, DateOnly ReadyDate, DateOnly? DeliverBy, GeoPoint? PickupAt = null, GeoPoint? DropAt = null, TimeOnly? WindowFrom = null, TimeOnly? WindowTo = null,
    OrderPriority Priority = OrderPriority.Normal, string? ProductCategory = null, HandlingType Handling = HandlingType.Standard,
    bool IsHazardous = false, bool IsStackable = true, decimal? LongestItemM = null, TimeOnly? PickupWindowFrom = null, TimeOnly? PickupWindowTo = null);

public sealed record SuggestedLoad(
    IReadOnlyList<Guid> OrderIds,
    string PickupCity,
    string PickupState,
    IReadOnlyList<string> Drops,
    decimal TotalWeightKg,
    decimal? TotalVolumeCbm,
    VehicleOption? Vehicle,
    decimal Utilization,
    FreightMode Suggested,
    DateOnly EarliestReady,
    DateOnly? EarliestDeadline,
    IReadOnlyList<Guid> BackhaulOrderIds,
    string? Warning);

/// <summary>
/// Groups open forward orders into sensible loads: same pickup, ready within a few days of each other, going to the same
/// state (so one run can serve them), packed into the largest vehicle first-fit-decreasing. Reverse orders that travel
/// the opposite way are offered as return loads. Rule-based and deterministic; it suggests, a planner decides.
/// </summary>
public static class ConsolidationPlanner
{
    public const int ReadyWindowDays = 2;

    /// <summary>A load filling less than this of the best-fitting truck is suggested as part load instead.</summary>
    public const decimal MinFtlUtilization = 0.6m;

    public static IReadOnlyList<SuggestedLoad> Suggest(IEnumerable<PlannableOrder> openOrders, IEnumerable<VehicleTypeInfo> vehicleTypes)
    {
        var types = vehicleTypes.Where(t => t.IsActive && t.PayloadKg > 0).OrderBy(t => t.PayloadKg).ToList();
        if (types.Count == 0)
        {
            return [];
        }

        var all = openOrders.ToList();
        var forward = all.Where(o => o.Direction == OrderDirection.Forward).ToList();
        var reverse = all.Where(o => o.Direction == OrderDirection.Reverse).ToList();
        var largest = types[^1];
        var loads = new List<SuggestedLoad>();

        foreach (var pickup in forward.GroupBy(o => (o.PickupState, o.PickupCity)).OrderBy(g => g.Key.PickupState, StringComparer.Ordinal).ThenBy(g => g.Key.PickupCity, StringComparer.Ordinal))
        {
            foreach (var cluster in ClusterByReadyDate(pickup))
            {
                foreach (var corridor in cluster.GroupBy(o => o.DropState).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    foreach (var bin in Pack(corridor, largest))
                    {
                        loads.Add(Describe(bin, types, reverse));
                    }
                }
            }
        }

        return loads
            .OrderBy(l => l.EarliestDeadline ?? DateOnly.MaxValue)
            .ThenBy(l => l.EarliestReady)
            .ThenBy(l => l.PickupState, StringComparer.Ordinal)
            .ThenBy(l => l.PickupCity, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<List<PlannableOrder>> ClusterByReadyDate(IEnumerable<PlannableOrder> orders)
    {
        List<PlannableOrder>? current = null;
        DateOnly start = default;
        foreach (var order in orders.OrderBy(o => o.ReadyDate).ThenBy(o => o.Number, StringComparer.Ordinal))
        {
            if (current is null || order.ReadyDate.DayNumber - start.DayNumber > ReadyWindowDays)
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = [];
                start = order.ReadyDate;
            }

            current.Add(order);
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    /// <summary>First-fit-decreasing by weight into the largest vehicle; an order bigger than any vehicle gets its own load.</summary>
    private static List<List<PlannableOrder>> Pack(IEnumerable<PlannableOrder> orders, VehicleTypeInfo largest)
    {
        var bins = new List<(List<PlannableOrder> Orders, decimal Weight, decimal Volume)>();
        foreach (var order in orders.OrderByDescending(o => o.WeightKg).ThenBy(o => o.DeliverBy ?? DateOnly.MaxValue).ThenBy(o => o.Number, StringComparer.Ordinal))
        {
            var volume = order.VolumeCbm ?? 0m;
            var placed = false;
            for (var i = 0; i < bins.Count; i++)
            {
                var (list, weight, vol) = bins[i];
                var weightFits = weight + order.WeightKg <= largest.PayloadKg;
                var volumeFits = largest.VolumeCbm is not { } cap || vol + volume <= cap;
                if (weightFits && volumeFits)
                {
                    list.Add(order);
                    bins[i] = (list, weight + order.WeightKg, vol + volume);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                bins.Add(([order], order.WeightKg, volume));
            }
        }

        return bins.Select(b => b.Orders).ToList();
    }

    private static SuggestedLoad Describe(List<PlannableOrder> bin, IReadOnlyList<VehicleTypeInfo> types, IReadOnlyList<PlannableOrder> reverse)
    {
        var weight = bin.Sum(o => o.WeightKg);
        var withVolume = bin.Where(o => o.VolumeCbm.HasValue).ToList();
        decimal? volume = withVolume.Count == 0 ? null : withVolume.Sum(o => o.VolumeCbm!.Value);
        var sizing = VehicleSizer.Recommend(weight, volume, types);
        var vehicle = sizing.Recommended;
        var utilization = vehicle is null ? 1m : Math.Max(vehicle.WeightUtilization, vehicle.VolumeUtilization ?? 0m);

        var drops = bin.OrderBy(o => o.DropCity, StringComparer.Ordinal).Select(o => $"{o.DropCity}, {o.DropState}").Distinct().ToList();
        var first = bin[0];
        var dropCities = bin.Select(o => (o.DropState, o.DropCity)).ToHashSet();
        var backhaul = reverse
            .Where(r => dropCities.Contains((r.PickupState, r.PickupCity)) && r.DropState == first.PickupState && r.DropCity == first.PickupCity)
            .OrderBy(r => r.Number, StringComparer.Ordinal)
            .Select(r => r.Id)
            .ToList();
        var deadlines = bin.Where(o => o.DeliverBy.HasValue).Select(o => o.DeliverBy!.Value).ToList();
        DateOnly? earliestDeadline = deadlines.Count == 0 ? null : deadlines.Min();

        return new SuggestedLoad(
            bin.OrderBy(o => o.DropCity, StringComparer.Ordinal).ThenBy(o => o.Number, StringComparer.Ordinal).Select(o => o.Id).ToList(),
            first.PickupCity, first.PickupState, drops, weight, volume, vehicle, utilization,
            utilization >= MinFtlUtilization && vehicle is not null ? FreightMode.Ftl : FreightMode.Ptl,
            bin.Min(o => o.ReadyDate), earliestDeadline, backhaul, sizing.Warning);
    }
}
