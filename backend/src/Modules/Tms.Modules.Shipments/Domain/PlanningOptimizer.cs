using System.Diagnostics;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

/// <param name="Locked">Vehicles the planner has locked; they are carried into the result unchanged and their orders are not in <see cref="Orders"/>.</param>
public sealed record PlanningInput(
    DateOnly Date, IReadOnlyList<PlannableOrder> Orders, IReadOnlyList<VehicleTypeInfo> VehicleTypes, PlanOptions Options, IReadOnlyList<PlannedVehicle> Locked,
    IReadOnlyList<CompatibilityPair>? IncompatiblePairs = null,
    Func<string, Task>? Progress = null);

/// <summary>
/// Turns orders into a plan. Behind an interface so the rule-based implementation can later be replaced by (or call out to)
/// a heavier solver in a background worker without touching the callers.
/// </summary>
public interface IPlanningOptimizer
{
    Task<PlanSnapshot> OptimizeAsync(PlanningInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Builds one vehicle exactly as a planner specified (which orders, which vehicle type, whether to keep the given stop order)
    /// and validates it. Used for manual edits: a violation is returned as an error, never silently fixed.
    /// </summary>
    Task<Tms.SharedKernel.Results.Result<PlannedVehicle>> BuildVehicleAsync(PinnedGroup group, PlanningInput context, CancellationToken cancellationToken);

    /// <summary>Prices every way a group of orders could move (each full-truck vehicle type, and part load) and explains the pick.</summary>
    Task<GroupComparison> CompareAsync(
        IReadOnlyList<PlannableOrder> group, DateOnly date, IReadOnlyList<VehicleTypeInfo> types, PlanOptions options, CancellationToken cancellationToken);
}

/// <param name="Forward">Outbound orders; with <paramref name="KeepOrder"/> they are visited in this order.</param>
/// <param name="Returns">Reverse orders to collect on the way back. They must still fit (capacity, time, detour).</param>
/// <param name="VehicleTypeId">Force this full-truck type; null lets the optimizer choose (or use <paramref name="Mode"/>).</param>
/// <param name="Mode">Force part load (or full truck) when no vehicle type is named.</param>
public sealed record PinnedGroup(
    IReadOnlyList<PlannableOrder> Forward, IReadOnlyList<PlannableOrder> Returns, Guid? VehicleTypeId, FreightMode? Mode, bool KeepOrder,
    Guid? RequiredVehicleId = null, Guid? RequiredDriverId = null);

public sealed record GroupComparison(
    IReadOnlyList<PlanAlternative> Alternatives, PlanAlternative? Chosen, string Reason, IReadOnlyList<VehicleTypeEvaluation> Sizing, string? FailureCode);

/// <summary>
/// Rule-based construction: group → sequence the stops → size → price every feasible way to move the group → pick by objective
/// → explain. Consolidation is kept only when it is cheaper than separate trips; return pickups are attached to forward routes
/// when capacity, time and detour allow. It is a constructive heuristic, so a plan is reported as Feasible, never as proven optimal.
/// </summary>
public sealed class RuleBasedPlanningOptimizer(
    IFreightQuoteService quotes, IRoutingProvider? routing = null, IFleetDirectory? fleet = null, ITransporterDirectory? transporterDirectory = null,
    ITransporterPlanningPolicy? policy = null) : IPlanningOptimizer
{
    private readonly Dictionary<string, TransporterStanding> _standings = [];
    // Fleet and compatibility state belongs to one run; both are replaced at the start of each public operation.
    private FleetAllocator _fleet = new(fleet, transporterDirectory);
    private CompatibilityPolicy _policy = CompatibilityPolicy.None;

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    // Within one run the same lane, load and vehicle are priced again and again (each order alone, then in every group it joins).
    // The optimizer is created per request, so these never outlive the run or cross tenants.
    private readonly Dictionary<string, FreightQuoteSet> _quoteCache = [];
    private readonly Dictionary<Guid, Evaluation> _soloCache = [];

    private async Task<Evaluation> SoloAsync(PlannableOrder order, PlanningInput input, CancellationToken cancellationToken)
    {
        if (!_soloCache.TryGetValue(order.Id, out var evaluation))
        {
            evaluation = await EvaluateAsync([order], input.Date, input.VehicleTypes, input.Options, cancellationToken);
            _soloCache[order.Id] = evaluation;
        }

        return evaluation;
    }

    /// <summary>
    /// What a separate trip for this order would cost, as the yardstick for a backhaul saving. It is a price, not a booking, so it does
    /// not need a vehicle to be free.
    /// </summary>
    private Task<Evaluation> ReferenceAsync(PlannableOrder order, PlanOptions options, DateOnly date, IReadOnlyList<VehicleTypeInfo> types, CancellationToken cancellationToken) =>
        EvaluateAsync([order], date, types, options with { RequireAvailableVehicle = false }, cancellationToken);

    private static string BarredNote(Dictionary<Guid, (string Name, string Reason)> barred) =>
        barred.Count == 0 ? string.Empty : $" Not offered the load: {string.Join(" ", barred.Values.Select(b => $"{b.Name} ({b.Reason.TrimEnd('.')}).")) }";

    private async Task<IReadOnlyDictionary<Guid, TransporterStanding>> StandingsAsync(
        IReadOnlyList<FreightQuoteResult> quotes, DateOnly date, PlannableOrder first, PlannableOrder drop, FreightMode mode, bool urgent, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TransporterStanding>();
        if (policy is null)
        {
            return result;
        }

        string Key(Guid id) => string.Join('|', id, date, first.PickupState, first.PickupCity, drop.DropState, drop.DropCity, mode, urgent);
        var missing = new List<Guid>();
        foreach (var id in quotes.Select(q => q.TransporterId).Distinct())
        {
            if (_standings.TryGetValue(Key(id), out var known))
            {
                result[id] = known;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            foreach (var (id, standing) in await policy.GetStandingsAsync(missing, date, first.PickupState, first.PickupCity, drop.DropState, drop.DropCity, mode, urgent, cancellationToken))
            {
                _standings[Key(id)] = standing;
                result[id] = standing;
            }
        }

        return result;
    }

    private async Task<FreightQuoteSet> PriceAsync(FreightQuoteRequest request, CancellationToken cancellationToken)
    {
        var key = string.Join('|', request.Date, request.OriginState, request.OriginCity, request.DestinationState, request.DestinationCity, request.VehicleTypeId, request.Mode, request.WeightKg, request.VolumeCbm, request.DistanceKm, request.Drops);
        if (!_quoteCache.TryGetValue(key, out var set))
        {
            set = await quotes.QuoteAsync(request, cancellationToken);
            _quoteCache[key] = set;
        }

        return set;
    }

    /// <summary>What a planned full-truck vehicle carries and where it starts, kept so return pickups can be fitted onto it afterwards.</summary>
    private sealed class Carriage(IReadOnlyList<PlannableOrder> ordered, int? payloadKg, decimal? volumeCapacity, GeoPoint depot, DateTimeOffset departure)
    {
        public IReadOnlyList<PlannableOrder> Ordered { get; } = ordered;

        public int? PayloadKg { get; } = payloadKg;

        public decimal? VolumeCapacity { get; } = volumeCapacity;

        public GeoPoint Depot { get; } = depot;

        public DateTimeOffset Departure { get; } = departure;

        public List<PlannableOrder> Returns { get; } = [];
    }

    private sealed record Outcome(List<PlannedVehicle> Vehicles, List<UnplannedOrder> Unplanned, Dictionary<Guid, Carriage> Carriages);

    public async Task<PlanSnapshot> OptimizeAsync(PlanningInput input, CancellationToken cancellationToken)
    {
        _fleet = new FleetAllocator(fleet, transporterDirectory);
        _fleet.Reserve(input.Locked);
        _policy = new CompatibilityPolicy(input.IncompatiblePairs ?? [], input.Options.SeparateHazardous);
        var clock = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(Math.Max(1, input.Options.TimeBudgetSeconds));
        var vehicles = new List<PlannedVehicle>(input.Locked);
        var unplanned = new List<UnplannedOrder>();
        var carriages = new Dictionary<Guid, Carriage>();
        var timedOut = false;

        var (forwardGroups, reverse) = BuildGroups(input);
        async Task Report(string message)
        {
            if (input.Progress is { } progress)
            {
                await progress(message);
            }
        }

        await Report($"Planning {input.Orders.Count} order(s) in {forwardGroups.Count} group(s), {reverse.Count} return order(s).");

        async Task PlanAsync(List<PlannableOrder> group)
        {
            if (clock.Elapsed > budget)
            {
                timedOut = true;
                unplanned.AddRange(group.Select(o => Unplanned(o, UnplannedCodes.TimeLimit, "The planning time limit was reached before this order was reached.")));
                return;
            }

            var outcome = await PlanGroupAsync(group, input, cancellationToken);
            vehicles.AddRange(outcome.Vehicles);
            unplanned.AddRange(outcome.Unplanned);
            foreach (var (key, value) in outcome.Carriages)
            {
                carriages[key] = value;
            }
        }

        var done = 0;
        foreach (var group in forwardGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PlanAsync(group);
            done++;
            if (forwardGroups.Count <= 10 || done % Math.Max(1, forwardGroups.Count / 10) == 0 || done == forwardGroups.Count)
            {
                await Report($"Planned {done} of {forwardGroups.Count} group(s): {vehicles.Count} vehicle(s), {unplanned.Count} order(s) unplanned so far.");
            }
        }

        var leftover = reverse;
        if (input.Options.AllowBackhaul && routing is not null && !timedOut && reverse.Count > 0)
        {
            await Report($"Attaching {reverse.Count} return pickup(s) to trucks already heading back.");
            leftover = await AttachReturnsAsync(reverse, vehicles, carriages, input, cancellationToken);
        }

        foreach (var order in leftover)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PlanAsync([order]);
        }

        var status = timedOut ? SolverStatus.TimeLimitReached
            : vehicles.Count == 0 && unplanned.Count > 0 ? SolverStatus.Infeasible
            : SolverStatus.Feasible;
        var message = status switch
        {
            SolverStatus.TimeLimitReached => "Time limit reached: this is the best plan built so far; the remaining orders are listed as unplanned.",
            SolverStatus.Infeasible => "No order could be planned.",
            _ => "Rule-based plan: every vehicle respects payload, volume, windows and deadlines. It is feasible, not proven optimal.",
        };
        await Report($"Finished in {clock.ElapsedMilliseconds} ms: {vehicles.Count} vehicle(s), {unplanned.Count} unplanned. {message}");
        return PlanSnapshot.Build(status, message, vehicles, unplanned);
    }

    /// <summary>Forward groups (consolidated or single, split to the stop limit) and the reverse orders, which are handled separately.</summary>
    private (List<List<PlannableOrder>> Forward, List<PlannableOrder> Reverse) BuildGroups(PlanningInput input)
    {
        var groups = new List<List<PlannableOrder>>();
        var forward = input.Orders.Where(o => o.Direction == OrderDirection.Forward).ToList();
        var reverse = input.Orders.Where(o => o.Direction == OrderDirection.Reverse).OrderByDescending(o => o.Priority).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();

        if (input.Options.AllowConsolidation)
        {
            var byId = forward.ToDictionary(o => o.Id);
            groups.AddRange(ConsolidationPlanner.Suggest(forward, input.VehicleTypes).Select(l => l.OrderIds.Select(id => byId[id]).ToList()));
        }
        else
        {
            groups.AddRange(forward.OrderBy(o => o.Number, StringComparer.Ordinal).Select(o => new List<PlannableOrder> { o }));
        }

        // Products that must not share a vehicle (hazardous apart from the rest, tenant rules) are separated before anything is priced.
        var maxStops = Math.Max(1, input.Options.MaxStops);
        var compatible = groups.SelectMany(g => _policy.Partition(g)).SelectMany(g => g.Chunk(maxStops).Select(c => c.ToList()));

        // Urgent work is planned first (it matters when the time budget runs out), then the tightest deadline.
        var ordered = compatible
            .OrderByDescending(g => g.Max(o => (int)o.Priority))
            .ThenBy(g => g.Min(o => o.DeliverBy ?? DateOnly.MaxValue))
            .ThenBy(g => g[0].Number, StringComparer.Ordinal)
            .ToList();
        return (ordered, reverse);
    }

    // ---- planning a group

    private async Task<Outcome> PlanGroupAsync(List<PlannableOrder> group, PlanningInput input, CancellationToken cancellationToken)
    {
        var outcome = new Outcome([], [], []);
        await PlanIntoAsync(outcome, group, input, cancellationToken);
        return outcome;
    }

    private async Task PlanIntoAsync(Outcome outcome, List<PlannableOrder> group, PlanningInput input, CancellationToken cancellationToken)
    {
        var together = await EvaluateAsync(group, input.Date, input.VehicleTypes, input.Options, cancellationToken);

        if (group.Count == 1)
        {
            await PlaceAsync(outcome, group, together, null, input, cancellationToken);
            return;
        }

        // Consolidation has to pay for itself: compare with moving each order on its own.
        var solo = new List<Evaluation>();
        foreach (var order in group)
        {
            solo.Add(await SoloAsync(order, input, cancellationToken));
        }

        var soloTotal = solo.All(e => e.Chosen?.Total is not null) ? solo.Sum(e => e.Chosen!.Total!.Value) : (decimal?)null;
        var pays = together.Chosen is not null && (soloTotal is null || together.Chosen.Total!.Value <= soloTotal.Value);

        if (pays)
        {
            var soloKm = solo.Where(e => e.Route is not null).Select(e => e.Route!.Sequence.TotalKm).DefaultIfEmpty().Max();
            var soloMinutes = solo.Where(e => e.Route is not null).Select(e => DrivingMinutes(e.Route!)).DefaultIfEmpty().Max();
            var comparable = together.Route is not null && solo.All(e => e.Route is not null);
            await PlaceAsync(outcome, group, together, null, input, cancellationToken, new ConsolidationMetrics(
                soloTotal, soloTotal is { } t ? Math.Max(0, t - together.Chosen!.Total!.Value) : null,
                comparable ? Math.Round(together.Route!.Sequence.TotalKm - soloKm, 1) : null,
                comparable ? Math.Round(DrivingMinutes(together.Route!) - soloMinutes) : null));
            return;
        }

        // The whole group is too big for any vehicle that has a rate, or costs more than separate trips. Smaller groups often work
        // (the same load as two trucks), so split and try each half before giving up on consolidating at all.
        if (group.Count > 2)
        {
            var (left, right) = Halve(group);
            await PlanIntoAsync(outcome, left, input, cancellationToken);
            await PlanIntoAsync(outcome, right, input, cancellationToken);
            return;
        }

        var why = together.Chosen is null
            ? $"They could not move together ({together.FailureMessage}), so each travels on its own."
            : $"Moving them together would cost ₹{together.Chosen.Total!.Value - soloTotal!.Value:N2} more than separate trips, so each travels on its own.";
        for (var i = 0; i < group.Count; i++)
        {
            await PlaceAsync(outcome, [group[i]], solo[i], why, input, cancellationToken);
        }
    }

    /// <summary>Two groups of about equal weight (heaviest first, always onto the lighter side), each keeping the original order.</summary>
    private static (List<PlannableOrder> Left, List<PlannableOrder> Right) Halve(List<PlannableOrder> group)
    {
        var left = new HashSet<Guid>();
        decimal leftWeight = 0, rightWeight = 0;
        foreach (var o in group.OrderByDescending(o => o.WeightKg).ThenBy(o => o.Number, StringComparer.Ordinal))
        {
            if (leftWeight <= rightWeight)
            {
                left.Add(o.Id);
                leftWeight += o.WeightKg;
            }
            else
            {
                rightWeight += o.WeightKg;
            }
        }

        return (group.Where(o => left.Contains(o.Id)).ToList(), group.Where(o => !left.Contains(o.Id)).ToList());
    }

    private sealed record ConsolidationMetrics(decimal? SeparateCost, decimal? Saving, double? AdditionalKm, double? AdditionalMinutes);

    private async Task PlaceAsync(
        Outcome outcome, List<PlannableOrder> group, Evaluation e, string? note, PlanningInput input, CancellationToken cancellationToken, ConsolidationMetrics? consolidation = null,
        bool retried = false)
    {
        if (e.Chosen is null)
        {
            outcome.Unplanned.AddRange(group.Select(o => Unplanned(o, e.FailureCode!, e.FailureMessage!)));
            return;
        }

        var chosen = e.Chosen;
        var vehicle = chosen.Vehicle;
        var driver = chosen.Driver;
        if (chosen.Mode == FreightMode.Ftl && _fleet.Enabled && chosen.VehicleTypeId is { } typeId && chosen.TransporterId is { } transporterId)
        {
            // The pick was made when this group was priced; an earlier trip in the same plan may have taken that vehicle or driver since.
            if (vehicle is null || driver is null || _fleet.IsTaken(vehicle.Id) || _fleet.DriverTaken(driver.Id))
            {
                if (input.Options.RequireAvailableVehicle && !retried)
                {
                    // An earlier trip in this plan took that vehicle or driver: price the group again, so the next-best option that is still free is used.
                    var fresh = await EvaluateAsync(group, input.Date, input.VehicleTypes, input.Options, cancellationToken);
                    await PlaceAsync(outcome, group, fresh, note, input, cancellationToken, consolidation, retried: true);
                    return;
                }

                var day = DateOnly.FromDateTime((e.Route?.Departure ?? new DateTimeOffset(input.Date.Year, input.Date.Month, input.Date.Day, 0, 0, 0, India)).DateTime);
                var again = await _fleet.SuggestAsync(transporterId, typeId, chosen.VehicleTypeName ?? "vehicle", day, null, null, cancellationToken);
                if (!again.Available && input.Options.RequireAvailableVehicle)
                {
                    outcome.Unplanned.AddRange(group.Select(o => Unplanned(o, UnplannedCodes.NoAvailableVehicle, again.Problem!)));
                    return;
                }

                vehicle = again.Vehicle;
                driver = again.Driver;
            }

            _fleet.Reserve(vehicle, driver);
        }

        var planned = Describe(group, e, consolidation, note, chosen.Transporter, vehicle, driver);
        outcome.Vehicles.Add(planned);
        if (e.Route is { } route && planned.Mode == FreightMode.Ftl && group.All(o => o.Direction == OrderDirection.Forward))
        {
            var type = e.Sizing.FirstOrDefault(s => s.VehicleTypeId == chosen.VehicleTypeId);
            outcome.Carriages[planned.Key] = new Carriage(route.Ordered, type?.PayloadKg, type?.VolumeCbm, route.Depot, route.Departure);
        }
    }

    // ---- evaluating a group

    private sealed record Evaluation(
        IReadOnlyList<PlanAlternative> Alternatives, PlanAlternative? Chosen, IReadOnlyList<VehicleTypeEvaluation> Sizing,
        string? FailureCode, string? FailureMessage, string Reason, RouteInfo? Route = null);

    /// <summary>The group's road route in the best stop order found, with when the truck would leave and reach each stop.</summary>
    private sealed record RouteInfo(
        IReadOnlyList<PlannableOrder> Ordered, SequenceResult Sequence, DistanceMatrix Matrix, GeoPoint Depot, DateTimeOffset Departure,
        double FtlTransitHours, IReadOnlyList<PlannedStop> Stops);

    private static double DrivingMinutes(RouteInfo route)
    {
        double minutes = 0;
        var at = 0;
        foreach (var index in route.Sequence.Order)
        {
            minutes += route.Matrix.Minutes[at, index + 1];
            at = index + 1;
        }

        return minutes;
    }

    /// <summary>Latest acceptable arrival for an order: the end of its deliver-by day, or the close of its delivery window that day.</summary>
    private static DateTimeOffset? Cutoff(PlannableOrder o) =>
        o.DeliverBy is not { } by
            ? null
            : o.WindowTo is { } close
                ? new DateTimeOffset(by.Year, by.Month, by.Day, close.Hour, close.Minute, 0, India)
                : new DateTimeOffset(by.Year, by.Month, by.Day, 0, 0, 0, India).AddDays(1);

    private async Task<RouteInfo?> BuildRouteAsync(IReadOnlyList<PlannableOrder> group, DateOnly date, PlanOptions options, CancellationToken cancellationToken, bool keepOrder = false)
    {
        if (routing is null || group[0].PickupAt is not { } pickup || group.Any(o => o.DropAt is null))
        {
            return null; // no coordinates: planning still works, it just has no distance, sequence, transit or SLA check
        }

        var points = new List<GeoPoint> { pickup };
        points.AddRange(group.Select(o => o.DropAt!));
        var matrix = await routing.GetMatrixAsync(points, cancellationToken);

        var start = group.Max(o => o.ReadyDate) > date ? group.Max(o => o.ReadyDate) : date;
        var departure = new DateTimeOffset(start.Year, start.Month, start.Day, Math.Clamp(options.DepartureHour, 0, 23), 0, 0, India);
        var stops = group.Select(o => new SequenceStop(o.Number, StopRole.Delivery, o.WeightKg, o.VolumeCbm, options.EnforceDeadlines ? Cutoff(o) : null, o.WindowFrom, o.WindowTo)).ToList();
        var volume = group.Where(o => o.VolumeCbm.HasValue).Sum(o => o.VolumeCbm!.Value);
        var problem = new SequenceProblem(matrix, stops, departure, options.StopServiceMinutes, false, group.Sum(o => o.WeightKg), volume, null, null);
        var sequence = keepOrder ? StopSequencer.Evaluate(problem, Enumerable.Range(0, group.Count).ToList()) : StopSequencer.Solve(problem);

        var ordered = sequence.Order.Select(i => group[i]).ToList();
        var list = new List<PlannedStop> { new(0, "Pickup", $"{group[0].PickupCity}, {group[0].PickupState}", pickup.Latitude, pickup.Longitude, null, departure) };
        foreach (var t in sequence.Timings)
        {
            var o = group[t.StopIndex];
            list.Add(new PlannedStop(list.Count, "Drop", $"{o.DropCity}, {o.DropState}", o.DropAt!.Latitude, o.DropAt.Longitude, t.Arrival, t.Departure, Math.Round(t.WaitMinutes)));
        }

        var transit = sequence.Timings.Count == 0 ? 0 : (sequence.Timings[^1].Departure - departure).TotalHours;
        return new RouteInfo(ordered, sequence, matrix, pickup, departure, transit, list);
    }

    private sealed record Forced(Guid? VehicleTypeId, FreightMode? Mode, bool KeepOrder, Guid? RequiredVehicleId = null, Guid? RequiredDriverId = null);

    private async Task<Evaluation> EvaluateAsync(
        IReadOnlyList<PlannableOrder> group, DateOnly date, IReadOnlyList<VehicleTypeInfo> types, PlanOptions options, CancellationToken cancellationToken, Forced? forced = null)
    {
        var weight = group.Sum(o => o.WeightKg);
        var withVolume = group.Where(o => o.VolumeCbm.HasValue).ToList();
        decimal? volume = withVolume.Count == 0 ? null : withVolume.Sum(o => o.VolumeCbm!.Value);
        var sizing = VehicleEvaluator.Evaluate(weight, volume, types, LoadRequirements.From(group));
        var fitting = sizing.Where(s => s.Accepted).ToList();

        if (sizing.Count == 0)
        {
            return Fail(sizing, UnplannedCodes.NoVehicleType, "No vehicle types are set up.");
        }

        // A planner who names a vehicle type gets exactly that type, or a clear reason it cannot carry the load.
        if (forced?.VehicleTypeId is { } forcedType)
        {
            var named = sizing.FirstOrDefault(s => s.VehicleTypeId == forcedType);
            if (named is null)
            {
                return Fail(sizing, UnplannedCodes.NoVehicleType, "That vehicle type is not available.");
            }

            if (!named.Accepted)
            {
                return Fail(sizing, named.RejectionCode ?? UnplannedCodes.NoVehicleType, $"{named.Name} cannot carry this load: {named.Reason}");
            }
        }

        // PTL is a shared truck: it needs no vehicle of its own, but each shipment still has to be small enough to be accepted as part load.
        var candidates = new List<(FreightMode Mode, VehicleTypeEvaluation? Type)>();
        if (forced?.VehicleTypeId is { } pinned)
        {
            candidates.Add((FreightMode.Ftl, fitting.First(f => f.VehicleTypeId == pinned)));
        }
        else if (forced?.Mode == FreightMode.Ptl)
        {
            candidates.Add((FreightMode.Ptl, null));
        }
        else
        {
            if (options.AllowFtl)
            {
                candidates.AddRange(fitting.Select(f => (FreightMode.Ftl, (VehicleTypeEvaluation?)f)));
            }

            // A part load still rides in somebody's truck, so a shipment bigger than any vehicle cannot move as part load either.
            // …and is capped by what a transporter will take as part load (a configurable limit, not a way of deciding between modes).
            if (options.AllowPtl && fitting.Count > 0 && weight <= options.MaxPtlWeightKg)
            {
                candidates.Add((FreightMode.Ptl, null));
            }
        }

        if (candidates.Count == 0)
        {
            // Big enough but unsuitable (dangerous goods, temperature control, a long item) is a different problem from too heavy.
            var unsuitable = sizing.Where(s => s.RejectionCode is UnplannedCodes.NotCompatible or UnplannedCodes.TooLong).OrderByDescending(s => s.PayloadKg).ToList();
            if (fitting.Count == 0 && unsuitable.Count > 0)
            {
                return Fail(sizing, unsuitable.All(s => s.RejectionCode == UnplannedCodes.TooLong) ? UnplannedCodes.TooLong : UnplannedCodes.NotCompatible,
                    $"No vehicle type is suitable for this load. {unsuitable[0].Reason}");
            }

            var rejected = sizing.OrderByDescending(s => s.PayloadKg).First();
            var code = fitting.Count == 0 ? rejected.RejectionCode ?? UnplannedCodes.NoVehicleType : UnplannedCodes.ModeNotAllowed;
            return Fail(sizing, code, fitting.Count == 0
                ? $"No vehicle type can carry this load. Largest ({rejected.Name}): {rejected.Reason}"
                : "Full-truck loads are not allowed in this run and the load cannot move as part load.");
        }

        var route = await BuildRouteAsync(group, date, options, cancellationToken, forced?.KeepOrder ?? false);
        if (route is { Sequence.Feasible: false })
        {
            return Fail(sizing, UnplannedCodes.DeadlineImpossible, $"No option can deliver on time: {route.Sequence.Violation}");
        }

        var ordered = route?.Ordered ?? group;
        var first = ordered[0];
        var drop = ordered[^1];
        decimal? distanceKm = route is null ? null : Math.Round((decimal)route.Sequence.TotalKm, 1);
        var priced = new List<(FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote)>();
        var barred = new Dictionary<Guid, (string Name, string Reason)>();
        var urgent = group.Any(o => o.Priority == OrderPriority.Urgent);
        foreach (var (mode, type) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var set = await PriceAsync(
                new FreightQuoteRequest(date, first.PickupState, first.PickupCity, drop.DropState, drop.DropCity, type?.VehicleTypeId, mode, weight, volume, distanceKm, group.Count),
                cancellationToken);

            // A transporter the planning rules bar (suspended, expired papers, restricted, avoided for urgent loads) is not offered the load, whatever it charges.
            // Among carriers at the same price, a preferred one wins, then the better-scored one.
            var standings = await StandingsAsync(set.Quotes, date, first, drop, mode, urgent, cancellationToken);
            foreach (var q in set.Quotes.Where(q => standings.TryGetValue(q.TransporterId, out var st) && !st.Allowed))
            {
                barred[q.TransporterId] = (q.TransporterName, standings[q.TransporterId].Reason ?? "not allowed");
            }

            var cheapestAllowed = set.Quotes.Where(q => !standings.TryGetValue(q.TransporterId, out var st) || st.Allowed)
                .OrderBy(q => q.Total)
                .ThenByDescending(q => standings.TryGetValue(q.TransporterId, out var st) && st.Preferred)
                .ThenByDescending(q => standings.TryGetValue(q.TransporterId, out var st) ? st.Score ?? 0m : 0m)
                .FirstOrDefault();
            priced.Add((mode, type, cheapestAllowed));
        }

        var rated = priced.Where(p => p.Quote is not null).ToList();
        if (rated.Count == 0)
        {
            return barred.Count > 0
                ? Fail(sizing, UnplannedCodes.TransporterRestricted, $"Every transporter with a rate for this load is barred: {string.Join(" ", barred.Values.Select(b => $"{b.Name}: {b.Reason}"))}")
                : Fail(sizing, UnplannedCodes.NoRate, "No active contract has a rate for this lane, vehicle and load.");
        }

        // Part load travels slower (terminal handling), so it can miss a deadline where a full truck makes it.
        (double? Hours, DateTimeOffset? Arrival, bool? Meets) Timing(FreightMode mode)
        {
            if (route is null)
            {
                return (null, null, null);
            }

            var extra = mode == FreightMode.Ptl ? options.PtlExtraTransitHours : 0;
            var timings = route.Sequence.Timings;
            bool? meets = null;
            for (var k = 0; k < timings.Count; k++)
            {
                if (Cutoff(route.Ordered[k]) is { } cutoff)
                {
                    var served = timings[k].Arrival.AddMinutes(timings[k].WaitMinutes).AddHours(extra);
                    meets = (meets ?? true) && served <= cutoff;
                }
            }

            var last = timings.Count == 0 ? (DateTimeOffset?)null : timings[^1].Arrival.AddMinutes(timings[^1].WaitMinutes).AddHours(extra);
            return (Math.Round(route.FtlTransitHours + extra, 1), last, meets);
        }

        var eligible = rated.Where(p => !(options.EnforceDeadlines && Timing(p.Mode).Meets == false)).ToList();
        if (eligible.Count == 0)
        {
            var earliest = rated.Select(p => Timing(p.Mode).Arrival).Where(a => a.HasValue).Min();
            return Fail(sizing, UnplannedCodes.DeadlineImpossible, $"No option can deliver on time: the earliest arrival is {earliest:dd MMM yyyy HH:mm}, after the deadline.");
        }

        // Who would actually drive it: a real vehicle and driver from the carrier's fleet, free and in order on the day.
        var day = DateOnly.FromDateTime((route?.Departure ?? new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, India)).DateTime);
        var fleetFor = new Dictionary<(Guid Type, Guid Transporter), FleetSuggestion>();
        var contacts = new Dictionary<Guid, AssignedTransporter?>();
        foreach (var p in rated)
        {
            if (!contacts.ContainsKey(p.Quote!.TransporterId))
            {
                contacts[p.Quote.TransporterId] = await _fleet.TransporterAsync(p.Quote.TransporterId, cancellationToken);
            }

            if (p.Mode == FreightMode.Ftl && p.Type is not null && !fleetFor.ContainsKey((p.Type.VehicleTypeId, p.Quote.TransporterId)))
            {
                fleetFor[(p.Type.VehicleTypeId, p.Quote.TransporterId)] =
                    await _fleet.SuggestAsync(p.Quote.TransporterId, p.Type.VehicleTypeId, p.Type.Name, day, forced?.RequiredVehicleId, forced?.RequiredDriverId, cancellationToken);
            }
        }

        FleetSuggestion? FleetOf((FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote) p) =>
            p.Mode == FreightMode.Ftl && p.Type is not null && p.Quote is not null ? fleetFor.GetValueOrDefault((p.Type.VehicleTypeId, p.Quote.TransporterId)) : null;
        bool Free((FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote) p) => !options.RequireAvailableVehicle || FleetOf(p) is not { Available: false };

        var allocatable = eligible.Where(Free).ToList();
        if (allocatable.Count == 0)
        {
            var problems = eligible.Select(p => FleetOf(p)?.Problem).Where(x => x is not null).Distinct().ToList();
            return Fail(sizing, UnplannedCodes.NoAvailableVehicle, $"No vehicle is free for this load. {string.Join(" ", problems)}");
        }

        var best = allocatable.OrderBy(p => Score(p, allocatable, options.Objective)).ThenBy(p => p.Quote!.Total).First();
        var alternatives = priced.Select(p =>
        {
            var chosen = ReferenceEquals(p.Quote, best.Quote) && p.Mode == best.Mode && p.Type == best.Type;
            var (hours, arrival, meets) = Timing(p.Mode);
            var late = options.EnforceDeadlines && meets == false;
            var suggestion = FleetOf(p);
            var verdict = chosen ? "Chosen"
                : late ? $"Would arrive {arrival:dd MMM HH:mm}, after the deadline."
                : p.Quote is not null && !Free(p) ? suggestion!.Problem!
                : Verdict(p, best, options.Objective);
            return new PlanAlternative(
                p.Mode, p.Type?.VehicleTypeId, p.Type?.Name, p.Quote?.ContractId, p.Quote?.ContractReference, p.Quote?.TransporterId, p.Quote?.TransporterName,
                p.Quote?.Total, p.Quote?.Lines.Select(l => new PlanQuoteLine(l.Code, l.Description, l.Amount)).ToList() ?? [], chosen, verdict, hours, meets,
                p.Quote is null ? null : contacts.GetValueOrDefault(p.Quote.TransporterId), suggestion?.Vehicle, suggestion?.Driver);
        }).ToList();

        // Types that cannot carry the load are alternatives that were never feasible: say so rather than hide them.
        alternatives.AddRange(sizing.Where(s => !s.Accepted).Select(s =>
            new PlanAlternative(FreightMode.Ftl, s.VehicleTypeId, s.Name, null, null, null, null, null, [], false, s.Reason!)));

        var chosenAlt = alternatives.First(a => a.Chosen);
        return new Evaluation(alternatives, chosenAlt, sizing, null, null, Explain(chosenAlt, best.Type, rated.Count, options.Objective, group.Count) + BarredNote(barred), route);
    }

    public async Task<GroupComparison> CompareAsync(
        IReadOnlyList<PlannableOrder> group, DateOnly date, IReadOnlyList<VehicleTypeInfo> types, PlanOptions options, CancellationToken cancellationToken)
    {
        var e = await EvaluateAsync(group, date, types, options, cancellationToken);
        return new GroupComparison(e.Alternatives, e.Chosen, e.Chosen is null ? e.FailureMessage! : e.Reason, e.Sizing, e.FailureCode);
    }

    private static Evaluation Fail(IReadOnlyList<VehicleTypeEvaluation> sizing, string code, string message) =>
        new([], null, sizing, code, message, message);

    // ---- return pickups

    /// <summary>
    /// Fits reverse orders onto forward vehicles that start from where the return is delivered. A fit must keep the truck within
    /// payload and volume at every moment, meet deadlines, and stay within the allowed detour; the return load is charged at a
    /// share of a standalone return trip. Returns the reverse orders that could not be attached.
    /// </summary>
    private async Task<List<PlannableOrder>> AttachReturnsAsync(
        List<PlannableOrder> reverse, List<PlannedVehicle> vehicles, Dictionary<Guid, Carriage> carriages, PlanningInput input, CancellationToken cancellationToken)
    {
        var options = input.Options;
        var left = new List<PlannableOrder>();

        foreach (var ret in reverse)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ret.PickupAt is null || ret.DropAt is null)
            {
                left.Add(ret);
                continue;
            }

            var standalone = await ReferenceAsync(ret, options, input.Date, input.VehicleTypes, cancellationToken);
            if (standalone.Chosen?.Total is not { } standaloneCost)
            {
                left.Add(ret); // no price for the return lane: the standalone path reports why
                continue;
            }

            (int Index, Fit Fit)? best = null;
            for (var v = 0; v < vehicles.Count; v++)
            {
                var vehicle = vehicles[v];
                if (vehicle.IsLocked || !carriages.TryGetValue(vehicle.Key, out var carriage))
                {
                    continue;
                }

                var (candidate, _) = await TryFitAsync(vehicle, carriage, ret, options, cancellationToken);
                if (candidate is not null && (best is not { } b || candidate.Extra < b.Fit.Extra))
                {
                    best = (v, candidate);
                }
            }

            if (best is not { } fit)
            {
                left.Add(ret);
                continue;
            }

            var target = vehicles[fit.Index];
            var carriageUsed = carriages[target.Key];
            carriageUsed.Returns.Add(ret);
            var charge = Math.Round(standaloneCost * options.BackhaulChargePercent / 100m, 2);
            vehicles[fit.Index] = WithReturns(target, carriageUsed, fit.Fit.Result, fit.Fit.Points, fit.Fit.Problem, ret, standaloneCost, charge, fit.Fit.Extra, options);
        }

        return left;
    }

    private sealed record Fit(SequenceResult Result, List<GeoPoint> Points, SequenceProblem Problem, double Extra);

    /// <summary>
    /// Can <paramref name="ret"/> be collected by this vehicle on the way back? Checks that the vehicle starts where the return is
    /// delivered, stays within payload and volume at every moment, meets deadlines, and keeps the detour within the limit.
    /// </summary>
    private async Task<(Fit? Fit, string? Failure)> TryFitAsync(PlannedVehicle vehicle, Carriage carriage, PlannableOrder ret, PlanOptions options, CancellationToken cancellationToken)
    {
        if (ret.PickupAt is null || ret.DropAt is null)
        {
            return (null, $"{ret.Number} has no coordinates for its pickup or delivery.");
        }

        if (carriage.PayloadKg is null)
        {
            return (null, "Only a full truck with a known capacity can collect a return.");
        }

        if (vehicle.PickupCity != ret.DropCity || vehicle.PickupState != ret.DropState)
        {
            return (null, $"{ret.Number} is delivered to {ret.DropCity}, {ret.DropState}, not to where this truck starts ({vehicle.PickupCity}, {vehicle.PickupState}).");
        }

        if (_policy.Conflict([.. carriage.Ordered, .. carriage.Returns, ret]) is { } incompatible)
        {
            return (null, incompatible);
        }

        var returns = carriage.Returns.Append(ret).ToList();
        var points = new List<GeoPoint> { carriage.Depot };
        points.AddRange(carriage.Ordered.Select(o => o.DropAt!));
        points.AddRange(returns.Select(r => r.PickupAt!));
        var matrix = await routing!.GetMatrixAsync(points, cancellationToken);

        var stops = carriage.Ordered
            .Select(o => new SequenceStop(o.Number, StopRole.Delivery, o.WeightKg, o.VolumeCbm, options.EnforceDeadlines ? Cutoff(o) : null, o.WindowFrom, o.WindowTo))
            .Concat(returns.Select(r => new SequenceStop(r.Number, StopRole.ReturnPickup, r.WeightKg, r.VolumeCbm, options.EnforceDeadlines ? Cutoff(r) : null, r.PickupWindowFrom, r.PickupWindowTo)))
            .ToList();
        var forwardWeight = carriage.Ordered.Sum(o => o.WeightKg);
        var forwardVolume = carriage.Ordered.Where(o => o.VolumeCbm.HasValue).Sum(o => o.VolumeCbm!.Value);
        var problem = new SequenceProblem(matrix, stops, carriage.Departure, options.StopServiceMinutes, true, forwardWeight, forwardVolume, carriage.PayloadKg, carriage.VolumeCapacity, options.ReturnsAfterDeliveries);

        var solved = StopSequencer.Solve(problem);
        if (!solved.Feasible)
        {
            return (null, solved.Violation);
        }

        // Compare with the same truck simply going home after its last drop.
        var baseline = StopSequencer.Evaluate(problem with { Stops = stops.Take(carriage.Ordered.Count).ToList() }, Enumerable.Range(0, carriage.Ordered.Count).ToList());
        var extra = solved.TotalKm - baseline.TotalKm;
        return extra > options.MaxBackhaulExtraKm
            ? (null, $"Collecting {ret.Number} adds a {extra:0.#} km detour, more than the {options.MaxBackhaulExtraKm} km allowed.")
            : (new Fit(solved, points, problem, extra), null);
    }

    public async Task<Tms.SharedKernel.Results.Result<PlannedVehicle>> BuildVehicleAsync(PinnedGroup group, PlanningInput context, CancellationToken cancellationToken)
    {
        if (group.Forward.Count == 0)
        {
            return Tms.SharedKernel.Results.Error.Validation("planning.edit_invalid", "A vehicle needs at least one outbound order.");
        }

        var options = context.Options;
        _fleet = new FleetAllocator(fleet, transporterDirectory);
        _fleet.Reserve(context.Locked); // the other trips in the plan keep their vehicles and drivers
        _policy = new CompatibilityPolicy(context.IncompatiblePairs ?? [], options.SeparateHazardous);
        if (_policy.Conflict([.. group.Forward, .. group.Returns]) is { } clash)
        {
            return Tms.SharedKernel.Results.Error.Conflict("planning.edit_invalid", clash);
        }

        var e = await EvaluateAsync(group.Forward, context.Date, context.VehicleTypes, options, cancellationToken, new Forced(group.VehicleTypeId, group.Mode, group.KeepOrder, group.RequiredVehicleId, group.RequiredDriverId));
        if (e.Chosen is null)
        {
            return Tms.SharedKernel.Results.Error.Conflict("planning.edit_invalid", e.FailureMessage!);
        }

        var vehicle = Describe(group.Forward.ToList(), e, null, "Manually adjusted.", e.Chosen.Transporter, e.Chosen.Vehicle, e.Chosen.Driver);
        if (group.Returns.Count == 0)
        {
            return vehicle;
        }

        if (e.Route is not { } route || vehicle.Mode != FreightMode.Ftl)
        {
            return Tms.SharedKernel.Results.Error.Conflict("planning.edit_invalid", "Return pickups need a full truck with a known route (coordinates on the orders).");
        }

        var type = e.Sizing.FirstOrDefault(s => s.VehicleTypeId == e.Chosen.VehicleTypeId);
        var carriage = new Carriage(route.Ordered, type?.PayloadKg, type?.VolumeCbm, route.Depot, route.Departure);
        foreach (var ret in group.Returns)
        {
            var (fit, failure) = await TryFitAsync(vehicle, carriage, ret, options, cancellationToken);
            if (fit is null)
            {
                return Tms.SharedKernel.Results.Error.Conflict("planning.edit_invalid", $"Return pickup {ret.Number} no longer fits this vehicle: {failure}");
            }

            var standalone = await ReferenceAsync(ret, options, context.Date, context.VehicleTypes, cancellationToken);
            if (standalone.Chosen?.Total is not { } standaloneCost)
            {
                return Tms.SharedKernel.Results.Error.Conflict("planning.edit_invalid", $"There is no rate for the return lane of {ret.Number}, so its charge cannot be worked out.");
            }

            carriage.Returns.Add(ret);
            vehicle = WithReturns(vehicle, carriage, fit.Result, fit.Points, fit.Problem, ret, standaloneCost, Math.Round(standaloneCost * options.BackhaulChargePercent / 100m, 2), fit.Extra, options);
        }

        return vehicle;
    }

    private static PlannedVehicle WithReturns(
        PlannedVehicle v, Carriage carriage, SequenceResult result, List<GeoPoint> points, SequenceProblem problem,
        PlannableOrder added, decimal standaloneCost, decimal charge, double extraKm, PlanOptions options)
    {
        var returns = carriage.Returns;
        PlannableOrder OrderAt(int stopIndex) => stopIndex < carriage.Ordered.Count ? carriage.Ordered[stopIndex] : returns[stopIndex - carriage.Ordered.Count];

        var orders = result.Order.Select((index, k) =>
        {
            var o = OrderAt(index);
            var isReturn = index >= carriage.Ordered.Count;
            return new PlannedOrder(o.Id, o.Number, k + 1, isReturn ? o.PickupCity : o.DropCity, isReturn ? o.PickupState : o.DropState, o.WeightKg, o.VolumeCbm, isReturn ? "ReturnPickup" : "Delivery", o.Priority.ToString());
        }).ToList();

        var stops = new List<PlannedStop> { new(0, "Pickup", $"{v.PickupCity}, {v.PickupState}", carriage.Depot.Latitude, carriage.Depot.Longitude, null, carriage.Departure) };
        foreach (var t in result.Timings)
        {
            var o = OrderAt(t.StopIndex);
            var isReturn = t.StopIndex >= carriage.Ordered.Count;
            var at = points[t.StopIndex + 1];
            stops.Add(new PlannedStop(stops.Count, isReturn ? "ReturnPickup" : "Drop", isReturn ? $"{o.PickupCity}, {o.PickupState}" : $"{o.DropCity}, {o.DropState}", at.Latitude, at.Longitude, t.Arrival, t.Departure, Math.Round(t.WaitMinutes)));
        }

        stops.Add(new PlannedStop(stops.Count, "Return", $"{v.PickupCity}, {v.PickupState}", carriage.Depot.Latitude, carriage.Depot.Longitude, result.DepotArrival, null));

        double driving = 0;
        var from = 0;
        foreach (var index in result.Order)
        {
            driving += problem.Matrix.Minutes[from, index + 1];
            from = index + 1;
        }

        driving += problem.Matrix.Minutes[from, 0];

        var (loaded, empty) = LoadSplit(problem.Matrix, result, problem.InitialWeightKg);

        var returnWeight = returns.Sum(r => r.WeightKg);
        var peak = Math.Max(problem.InitialWeightKg, result.Timings.Select(t => t.OnboardWeightKg).DefaultIfEmpty().Max());
        var tonnes = (v.WeightKg + returnWeight) / 1000m;
        var cost = v.EstimatedCost + charge;
        var lines = v.CostLines.Append(new PlanQuoteLine("BACKHAUL", $"Return load {added.Number} ({options.BackhaulChargePercent}% of a standalone return trip)", charge)).ToList();
        var saving = (v.BackhaulSaving ?? 0m) + (standaloneCost - charge);

        return v with
        {
            Orders = orders,
            Stops = stops,
            DistanceKm = Math.Round(result.TotalKm, 1),
            DurationMinutes = Math.Round(driving),
            EstimatedCost = cost,
            CostLines = lines,
            WeightKg = v.WeightKg,
            WeightUtilisation = carriage.PayloadKg is { } payload and > 0 ? Math.Round(peak / payload, 4) : v.WeightUtilisation,
            CostPerTonneKm = result.TotalKm > 0 && tonnes > 0 ? Math.Round(cost / ((decimal)result.TotalKm * tonnes), 2) : v.CostPerTonneKm,
            BackhaulSaving = saving,
            LoadedKm = Math.Round(loaded, 1),
            EmptyKm = Math.Round(empty, 1),
            RouteNote = $"Includes the return to the depot. {added.Number} is collected on the way back (+{extraKm:0.#} km detour); it is charged at {options.BackhaulChargePercent}% of a standalone return trip (₹{standaloneCost:N2}), a rate you set for this run.",
            Reason = v.Reason + $" Return pickup {added.Number} was added on the way back, saving ₹{standaloneCost - charge:N2} against a separate return trip.",
        };
    }

    // ---- scoring and describing

    /// <summary>Lower is better. Cost and utilisation are normalised against the candidates so they can be combined.</summary>
    private static decimal Score((FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote) p, List<(FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote)> all, PlanObjective objective)
    {
        var cost = p.Quote!.Total;
        var minCost = Math.Max(all.Min(a => a.Quote!.Total), 0.01m);
        var fill = p.Type is null ? 0.5m : VehicleEvaluator.BindingUtilisation(p.Type); // PTL's shared truck has no fill of its own
        return objective switch
        {
            PlanObjective.MaximizeUtilisation => 1m - fill,
            PlanObjective.MinimizeVehicles => p.Mode == FreightMode.Ptl ? 0m : 1m - fill, // part load adds no vehicle
            PlanObjective.BalanceCostAndUtilisation => 0.5m * (cost / minCost) + 0.5m * (1m - fill),
            _ => cost, // MinimizeTotalCost, and MinimizeDistance until grouping is distance-driven
        };
    }

    private static string Verdict((FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote) p, (FreightMode Mode, VehicleTypeEvaluation? Type, FreightQuoteResult? Quote) best, PlanObjective objective)
    {
        if (p.Quote is null)
        {
            return "No contract rate for this option.";
        }

        var diff = p.Quote.Total - best.Quote!.Total;
        var label = p.Mode == FreightMode.Ptl ? "Part load" : p.Type?.Name ?? "Full truck";
        return diff > 0 ? $"{label} costs ₹{diff:N2} more."
            : diff < 0 ? $"{label} costs ₹{-diff:N2} less but ranks lower on the chosen objective."
            : $"{label} costs the same; ranked lower on the tie-break.";
    }

    private static string Explain(PlanAlternative chosen, VehicleTypeEvaluation? type, int rated, PlanObjective objective, int orders)
    {
        var what = chosen.Mode == FreightMode.Ptl ? "Part load" : $"{chosen.VehicleTypeName} full truck";
        var fill = type is null ? string.Empty : $" ({VehicleEvaluator.BindingUtilisation(type):P0} full)";
        var goal = objective switch
        {
            PlanObjective.MinimizeVehicles => "fewest vehicles",
            PlanObjective.MaximizeUtilisation => "highest utilisation",
            PlanObjective.BalanceCostAndUtilisation => "best balance of cost and utilisation",
            PlanObjective.MinimizeDistance => "lowest cost (distance does not differ between vehicles for one group)",
            _ => "lowest cost",
        };
        return $"{what}{fill} at ₹{chosen.Total:N2} was chosen for {orders} order(s): of {rated} priced option(s) it gives the {goal}.";
    }

    /// <summary>Kilometres run with goods on board against kilometres run empty, leg by leg, including the way back to the depot.</summary>
    private static (double Loaded, double Empty) LoadSplit(DistanceMatrix matrix, SequenceResult result, decimal initialWeightKg)
    {
        double loaded = 0, empty = 0;
        var onboard = initialWeightKg;
        var from = 0;
        for (var k = 0; k < result.Order.Count; k++)
        {
            var km = matrix.Km[from, result.Order[k] + 1];
            if (onboard > 0) { loaded += km; } else { empty += km; }

            onboard = result.Timings[k].OnboardWeightKg;
            from = result.Order[k] + 1;
        }

        if (result.DepotArrival is not null)
        {
            var home = matrix.Km[from, 0];
            if (onboard > 0) { loaded += home; } else { empty += home; }
        }

        return (loaded, empty);
    }

    private static PlannedVehicle Describe(
        List<PlannableOrder> group, Evaluation e, ConsolidationMetrics? consolidation, string? note,
        AssignedTransporter? transporter = null, AssignedVehicle? assignedVehicle = null, AssignedDriver? assignedDriver = null)
    {
        var chosen = e.Chosen!;
        var type = e.Sizing.FirstOrDefault(s => s.VehicleTypeId == chosen.VehicleTypeId);
        var weight = group.Sum(o => o.WeightKg);
        var withVolume = group.Where(o => o.VolumeCbm.HasValue).ToList();
        decimal? volume = withVolume.Count == 0 ? null : withVolume.Sum(o => o.VolumeCbm!.Value);
        var ordered = e.Route?.Ordered ?? group;
        var first = ordered[0];
        var isReverse = group[0].Direction == OrderDirection.Reverse;
        var km = e.Route is null ? (double?)null : Math.Round(e.Route.Sequence.TotalKm, 1);
        var saving = consolidation?.Saving;
        return new PlannedVehicle(
            Guid.CreateVersion7(), first.PickupCity, first.PickupState, chosen.Mode, chosen.VehicleTypeId, chosen.VehicleTypeName,
            type?.PayloadKg, type?.VolumeCbm, chosen.ContractId, chosen.ContractReference, chosen.TransporterId, chosen.TransporterName,
            chosen.Total!.Value, chosen.Lines, weight, volume,
            chosen.Mode == FreightMode.Ftl ? type?.WeightUtilisation : null, chosen.Mode == FreightMode.Ftl ? type?.VolumeUtilisation : null,
            ordered.Select((o, i) => new PlannedOrder(o.Id, o.Number, i + 1, o.DropCity, o.DropState, o.WeightKg, o.VolumeCbm, Priority: o.Priority.ToString())).ToList(),
            e.Alternatives, e.Reason, false, saving > 0 ? saving : null,
            DistanceKm: km,
            DurationMinutes: e.Route is null ? null : Math.Round(DrivingMinutes(e.Route)),
            RouteSource: e.Route?.Matrix.Source,
            TransitHours: chosen.TransitHours,
            CostPerTonneKm: e.Route is { } r && r.Sequence.TotalKm > 0 && weight > 0
                ? Math.Round(chosen.Total!.Value / ((decimal)r.Sequence.TotalKm * weight / 1000m), 2) : null,
            Stops: e.Route?.Stops,
            PlannedDeparture: e.Route?.Departure,
            SequenceMethod: e.Route is { } ro && group.Count > 1 ? ro.Sequence.Method : null,
            AdditionalKm: consolidation?.AdditionalKm,
            AdditionalMinutes: consolidation?.AdditionalMinutes,
            SeparateCost: consolidation?.SeparateCost,
            SavingPercent: consolidation is { SeparateCost: > 0, Saving: { } s } ? Math.Round(s / consolidation.SeparateCost.Value * 100, 1) : null,
            RouteNote: isReverse && e.Route is not null ? "Return trip: collected at the pickup and delivered to the destination." : note,
            Transporter: transporter,
            AssignedVehicle: assignedVehicle,
            AssignedDriver: assignedDriver,
            LengthUtilisation: chosen.Mode == FreightMode.Ftl ? type?.LengthUtilisation : null,
            LoadedKm: e.Route is { } lr ? Math.Round(LoadSplit(lr.Matrix, lr.Sequence, isReverse ? 0m : weight).Loaded, 1) : null,
            EmptyKm: e.Route is { } er ? Math.Round(LoadSplit(er.Matrix, er.Sequence, isReverse ? 0m : weight).Empty, 1) : null);
    }

    private static UnplannedOrder Unplanned(PlannableOrder order, string code, string reason) =>
        new(order.Id, order.Number, code, reason, SuggestionsFor(code));

    public static IReadOnlyList<string> SuggestionsFor(string code) => code switch
    {
        UnplannedCodes.PayloadExceeded => ["Split the order across two shipments", "Add a larger vehicle type in the vehicle master"],
        UnplannedCodes.VolumeExceeded => ["Split the order across two shipments", "Add a higher-volume vehicle type"],
        UnplannedCodes.NoRate => ["Add or extend a contract rate for this lane and vehicle", "Enable part load if a PTL rate exists", "Change the planning date to one a contract covers"],
        UnplannedCodes.ModeNotAllowed => ["Enable part load or full truck for this run"],
        UnplannedCodes.TimeLimit => ["Run planning again with fewer orders", "Increase the time limit"],
        UnplannedCodes.NoVehicleType => ["Set up vehicle types in the transporter master"],
        UnplannedCodes.DeadlineImpossible => ["Relax the deliver-by date or the delivery window", "Allow full truck (faster than part load)", "Plan an earlier date"],
        UnplannedCodes.NotCompatible => ["Add or approve a vehicle type that can carry this (hazardous / temperature-controlled)", "Check the product category and handling on the order"],
        UnplannedCodes.TooLong => ["Add a vehicle type with a longer body", "Correct the longest-item length on the order"],
        UnplannedCodes.NoAvailableVehicle => ["Register or return a vehicle of that type", "Plan a day when one is free", "Switch off \"require an available vehicle\" if the fleet list is incomplete"],
        UnplannedCodes.TransporterRestricted => ["Lift or end the planning rule, or renew the expired documents", "Add a contract with another transporter on this lane", "Plan the order as non-urgent if only urgent loads are avoided"],
        UnplannedCodes.Incompatible => ["Split the products onto separate vehicles", "Review the compatibility rules"],
        UnplannedCodes.LockConflict => ["Unlock the vehicle, sequence or order, then re-plan"],
        _ => ["Enable consolidation", "Change the planning date"],
    };
}
