using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

public sealed record MilkRunStopDef(
    Guid LocationId, string Label, string City, string State, GeoPoint Point, MilkRunStopType Type, int ServiceMinutes, TimeOnly? WindowFrom, TimeOnly? WindowTo);

public sealed record MilkRunDefinition(
    string Code, string Name, string DepotLabel, string DepotCity, string DepotState, GeoPoint Depot, Guid? PreferredVehicleTypeId, int MaxStops,
    int MaxDurationMinutes, TimeOnly DepartureTime, IReadOnlyList<MilkRunStopDef> Stops);

/// <param name="StopIndex">Zero-based index into <see cref="MilkRunDefinition.Stops"/>.</param>
public sealed record MilkRunOrder(Guid OrderId, string Number, int StopIndex, decimal WeightKg, decimal? VolumeCbm, DateOnly? DeliverBy);

/// <param name="KeepTemplateOrder">Visit stops in the template's planned order (skipping empty ones). Off: find the shortest order.</param>
public sealed record MilkRunOptions(bool KeepTemplateOrder = true, bool EnforceDeadlines = true);

public sealed record MilkRunInput(
    DateOnly Date, MilkRunDefinition Template, IReadOnlyList<MilkRunOrder> Orders, IReadOnlyList<VehicleTypeInfo> VehicleTypes, MilkRunOptions Options);

public sealed record MilkRunOrderLine(Guid OrderId, string Number, decimal WeightKg, decimal? VolumeCbm);

public sealed record MilkRunStopPlan(
    int Sequence, string Kind, string Label, IReadOnlyList<MilkRunOrderLine> Orders, decimal WeightKg, decimal? VolumeCbm,
    DateTimeOffset? Arrival, DateTimeOffset? Departure, double? WaitMinutes, decimal OnboardKg);

public sealed record MilkRunTrip(
    int Number,
    Guid? VehicleTypeId,
    string? VehicleTypeName,
    int? PayloadKg,
    decimal? VolumeCapacityCbm,
    Guid? ContractId,
    string? ContractReference,
    Guid? TransporterId,
    string? TransporterName,
    decimal? Cost,
    IReadOnlyList<PlanQuoteLine> CostLines,
    double DistanceKm,
    double DrivingMinutes,
    double TotalMinutes,
    DateTimeOffset Departure,
    DateTimeOffset ReturnAt,
    decimal PeakWeightKg,
    decimal? PeakVolumeCbm,
    decimal? WeightUtilisation,
    decimal? VolumeUtilisation,
    decimal? CostPerTonneKm,
    IReadOnlyList<MilkRunStopPlan> Stops,
    string SequenceMethod,
    double? TemplateOrderKm,
    double? ShortestOrderKm,
    IReadOnlyList<PlanAlternative> Alternatives,
    IReadOnlyList<string> Warnings,
    string Reason,
    RouteSource Source = RouteSource.Estimate,
    AssignedTransporter? Transporter = null,
    AssignedVehicle? Vehicle = null,
    AssignedDriver? Driver = null);

public sealed record MilkRunSkippedStop(int TemplateSequence, string Label, string Reason);

public sealed record MilkRunTotals(
    int Orders, int StopsServed, int StopsSkipped, int Trips, decimal InboundKg, decimal OutboundKg, double DistanceKm, decimal? Cost, decimal? CostPerTonneKm);

public sealed record MilkRunPlan(
    string Code, string Name, DateOnly Date, IReadOnlyList<MilkRunTrip> Trips, IReadOnlyList<MilkRunSkippedStop> Skipped,
    IReadOnlyList<UnplannedOrder> Unplanned, MilkRunTotals Totals, RouteSource? Source, IReadOnlyList<string> Warnings);

/// <summary>
/// Plans one day of a milk run. The template says where the truck goes; today's orders say what is actually collected and
/// delivered. Empty stops are skipped, the load decides the vehicle (not yesterday's choice), a day that does not fit one vehicle,
/// the stop limit or the duration limit is split into trips, and the result says how much the planned sequence costs against
/// the shortest one.
/// </summary>
public sealed class MilkRunPlanner(
    IFreightQuoteService quotes, IRoutingProvider routing, IFleetDirectory? fleet = null, ITransporterDirectory? transporterDirectory = null)
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private sealed record StopLoad(int Index, MilkRunStopDef Def, IReadOnlyList<MilkRunOrder> Orders)
    {
        public decimal WeightKg => Orders.Sum(o => o.WeightKg);

        public decimal? VolumeCbm => Orders.Any(o => o.VolumeCbm.HasValue) ? Orders.Where(o => o.VolumeCbm.HasValue).Sum(o => o.VolumeCbm!.Value) : null;

        public bool Inbound => Def.Type == MilkRunStopType.Pickup;
    }

    public async Task<MilkRunPlan> PlanAsync(MilkRunInput input, CancellationToken cancellationToken)
    {
        var template = input.Template;
        var warnings = new List<string>();
        var unplanned = new List<UnplannedOrder>();
        var trips = new List<MilkRunTrip>();
        var allocator = new FleetAllocator(fleet, transporterDirectory); // a vehicle or driver serves one trip a day

        var loads = template.Stops.Select((def, i) => new StopLoad(i, def, input.Orders.Where(o => o.StopIndex == i).ToList())).ToList();
        var skipped = loads.Where(l => l.Orders.Count == 0).Select(l => new MilkRunSkippedStop(l.Index + 1, l.Def.Label, "Nothing to collect or deliver today.")).ToList();
        var used = loads.Where(l => l.Orders.Count > 0).ToList();

        var types = input.VehicleTypes.Where(t => t.IsActive && t.PayloadKg > 0).OrderBy(t => t.PayloadKg).ToList();
        if (types.Count == 0)
        {
            warnings.Add("No vehicle types are set up.");
            unplanned.AddRange(used.SelectMany(l => l.Orders).Select(o => Unplanned(o, UnplannedCodes.NoVehicleType, "No vehicle types are set up.")));
            return Build(template, input.Date, [], skipped, unplanned, warnings);
        }

        var largest = types[^1];
        foreach (var load in used.ToList())
        {
            var tooHeavy = load.WeightKg > largest.PayloadKg;
            var tooBulky = largest.VolumeCbm is { } cap && load.VolumeCbm is { } v && v > cap;
            if (tooHeavy || tooBulky)
            {
                var reason = tooHeavy
                    ? $"{load.Def.Label} alone is {load.WeightKg:0.##} kg; the largest vehicle ({largest.Name}) carries {largest.PayloadKg} kg."
                    : $"{load.Def.Label} alone is {load.VolumeCbm:0.##} CBM; the largest vehicle ({largest.Name}) holds {largest.VolumeCbm:0.##} CBM.";
                unplanned.AddRange(load.Orders.Select(o => Unplanned(o, tooHeavy ? UnplannedCodes.PayloadExceeded : UnplannedCodes.VolumeExceeded, reason)));
                used.Remove(load);
            }
        }

        async Task PlanGroupAsync(List<StopLoad> group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EvaluateTripAsync(group, input, types, largest, allocator, cancellationToken);
            if (result.Trip is null)
            {
                unplanned.AddRange(group.SelectMany(g => g.Orders).Select(o => Unplanned(o, result.Code!, result.Message!)));
                return;
            }

            if (result.Trip.TotalMinutes > template.MaxDurationMinutes && group.Count >= 2)
            {
                // Too long for one day's limit: run it as two trips, each keeping the planned order.
                var half = (group.Count + 1) / 2;
                await PlanGroupAsync(group.Take(half).ToList());
                await PlanGroupAsync(group.Skip(half).ToList());
                return;
            }

            var trip = result.Trip;
            if (trip.TotalMinutes > template.MaxDurationMinutes)
            {
                trip = trip with { Warnings = [.. trip.Warnings, $"This trip takes {trip.TotalMinutes / 60:0.#} h, over the {template.MaxDurationMinutes / 60.0:0.#} h limit, and cannot be split further."] };
            }

            allocator.Reserve(trip.Vehicle, trip.Driver);
            trips.Add(trip);
        }

        foreach (var group in SplitByCapacity(used, largest, template.MaxStops))
        {
            await PlanGroupAsync(group);
        }

        var numbered = trips.Select((trip, i) => trip with { Number = i + 1 }).ToList();
        return Build(template, input.Date, numbered, skipped, unplanned, warnings);
    }

    /// <summary>Consecutive stops, in planned order, until the next one would overflow the largest vehicle or the stop limit.</summary>
    private static List<List<StopLoad>> SplitByCapacity(List<StopLoad> used, VehicleTypeInfo largest, int maxStops)
    {
        var groups = new List<List<StopLoad>>();
        List<StopLoad>? current = null;
        decimal inW = 0, outW = 0, inV = 0, outV = 0;
        foreach (var load in used)
        {
            var v = load.VolumeCbm ?? 0m;
            var overweight = (load.Inbound ? inW + load.WeightKg : outW + load.WeightKg) > largest.PayloadKg;
            var overvolume = largest.VolumeCbm is { } cap && (load.Inbound ? inV + v : outV + v) > cap;
            if (current is null || overweight || overvolume || current.Count >= maxStops)
            {
                current = [];
                groups.Add(current);
                inW = outW = inV = outV = 0;
            }

            current.Add(load);
            if (load.Inbound)
            {
                inW += load.WeightKg;
                inV += v;
            }
            else
            {
                outW += load.WeightKg;
                outV += v;
            }
        }

        return groups;
    }

    private static DateTimeOffset? Cutoff(DateOnly? by, TimeOnly? close) =>
        by is not { } d
            ? null
            : close is { } c ? new DateTimeOffset(d.Year, d.Month, d.Day, c.Hour, c.Minute, 0, India) : new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, India).AddDays(1);

    private sealed record TripResult(MilkRunTrip? Trip, string? Code, string? Message);

    private async Task<TripResult> EvaluateTripAsync(
        List<StopLoad> group, MilkRunInput input, IReadOnlyList<VehicleTypeInfo> types, VehicleTypeInfo largest, FleetAllocator allocator, CancellationToken cancellationToken)
    {
        var t = input.Template;
        var options = input.Options;
        var points = new List<GeoPoint> { t.Depot };
        points.AddRange(group.Select(g => g.Def.Point));
        var matrix = await routing.GetMatrixAsync(points, cancellationToken);

        var departure = new DateTimeOffset(input.Date.Year, input.Date.Month, input.Date.Day, t.DepartureTime.Hour, t.DepartureTime.Minute, 0, India);
        var stops = group.Select(g => new SequenceStop(
            g.Def.Label, g.Inbound ? StopRole.ReturnPickup : StopRole.Delivery, g.WeightKg, g.VolumeCbm,
            options.EnforceDeadlines ? g.Orders.Select(o => Cutoff(o.DeliverBy, g.Inbound ? null : g.Def.WindowTo)).Where(c => c.HasValue).Min() : null,
            g.Def.WindowFrom, g.Def.WindowTo, g.Def.ServiceMinutes)).ToList();

        var outbound = group.Where(g => !g.Inbound).Sum(g => g.WeightKg);
        var outboundVolume = group.Where(g => !g.Inbound).Sum(g => g.VolumeCbm ?? 0m);
        var inboundVolume = group.Where(g => g.Inbound).Sum(g => g.VolumeCbm ?? 0m);
        var problem = new SequenceProblem(matrix, stops, departure, 0, true, outbound, outboundVolume, largest.PayloadKg, largest.VolumeCbm, ReturnsAfterDeliveries: true);

        var planned = StopSequencer.Evaluate(problem, Enumerable.Range(0, group.Count).ToList());
        var shortest = StopSequencer.Solve(problem);
        var warnings = new List<string>();
        var chosen = options.KeepTemplateOrder ? planned : shortest;
        if (!chosen.Feasible && shortest.Feasible)
        {
            warnings.Add($"The planned order cannot be used today ({chosen.Violation}) so the stops were re-sequenced.");
            chosen = shortest;
        }

        if (!chosen.Feasible)
        {
            return new TripResult(null, UnplannedCodes.DeadlineImpossible, $"This run cannot be completed on time: {chosen.Violation}");
        }

        var inbound = group.Where(g => g.Inbound).Sum(g => g.WeightKg);
        var peakWeight = Math.Max(outbound, chosen.Timings.Select(x => x.OnboardWeightKg).DefaultIfEmpty().Max());
        var hasVolume = group.Any(g => g.VolumeCbm.HasValue);
        decimal? peakVolume = hasVolume ? Math.Max(outboundVolume, inboundVolume) : null;

        var sizing = VehicleEvaluator.Evaluate(peakWeight, peakVolume, types);
        var fitting = sizing.Where(s => s.Accepted).ToList();
        if (fitting.Count == 0)
        {
            return new TripResult(null, UnplannedCodes.PayloadExceeded, $"No vehicle carries {peakWeight:0.##} kg at once.");
        }

        var km = Math.Round(chosen.TotalKm, 1);
        var far = group.Select((g, i) => (Load: g, Position: i)).OrderByDescending(x => matrix.Km[0, x.Position + 1]).First().Load; // the lane is priced to the farthest stop
        var inboundOnly = group.All(g => g.Inbound); // goods travel stop → depot, so that is the lane to price
        var priced = new List<(VehicleTypeEvaluation Type, FreightQuoteResult? Quote)>();
        foreach (var type in fitting)
        {
            var set = await quotes.QuoteAsync(
                inboundOnly
                    ? new FreightQuoteRequest(input.Date, far.Def.State, far.Def.City, t.DepotState, t.DepotCity, type.VehicleTypeId, FreightMode.Ftl, peakWeight, peakVolume, (decimal)km, group.Count)
                    : new FreightQuoteRequest(input.Date, t.DepotState, t.DepotCity, far.Def.State, far.Def.City, type.VehicleTypeId, FreightMode.Ftl, peakWeight, peakVolume, (decimal)km, group.Count),
                cancellationToken);
            priced.Add((type, set.Quotes.OrderBy(q => q.Total).FirstOrDefault()));
        }

        var rated = priced.Where(p => p.Quote is not null).ToList();

        // Who would actually run it: a real vehicle and driver from the carrier, free and in order that day.
        var suggestions = new Dictionary<(Guid Type, Guid Transporter), FleetSuggestion>();
        var contacts = new Dictionary<Guid, AssignedTransporter?>();
        foreach (var (type, quote) in rated)
        {
            contacts.TryAdd(quote!.TransporterId, await allocator.TransporterAsync(quote.TransporterId, cancellationToken));
            if (!suggestions.ContainsKey((type.VehicleTypeId, quote.TransporterId)))
            {
                suggestions[(type.VehicleTypeId, quote.TransporterId)] =
                    await allocator.SuggestAsync(quote.TransporterId, type.VehicleTypeId, type.Name, input.Date, null, null, cancellationToken);
            }
        }

        FleetSuggestion? SuggestionOf((VehicleTypeEvaluation Type, FreightQuoteResult? Quote) p) =>
            p.Quote is null ? null : suggestions.GetValueOrDefault((p.Type.VehicleTypeId, p.Quote.TransporterId));
        var usable = rated.Where(p => SuggestionOf(p) is not { Available: false }).ToList();
        if (rated.Count > 0 && usable.Count == 0)
        {
            var problems = rated.Select(p => SuggestionOf(p)?.Problem).Where(x => x is not null).Distinct();
            return new TripResult(null, UnplannedCodes.NoAvailableVehicle, $"No vehicle is free for this run. {string.Join(" ", problems)}");
        }

        (VehicleTypeEvaluation Type, FreightQuoteResult? Quote) pick;
        if (usable.Count > 0)
        {
            // Cheapest wins; the usual vehicle breaks a tie. The vehicle is a result of today's load, not a fixed choice.
            pick = usable.OrderBy(p => p.Quote!.Total).ThenByDescending(p => p.Type.VehicleTypeId == t.PreferredVehicleTypeId).First();
        }
        else
        {
            pick = priced.FirstOrDefault(p => p.Type.VehicleTypeId == t.PreferredVehicleTypeId) is { } preferred && preferred.Type is not null ? preferred : priced[0];
            warnings.Add("No active contract has a rate for this run, so its cost is unknown.");
        }

        var alternatives = priced.Select(p =>
        {
            var isChosen = p.Type == pick.Type;
            var suggestion = SuggestionOf(p);
            var verdict = isChosen ? "Chosen"
                : p.Quote is null ? "No contract rate for this option."
                : suggestion is { Available: false } ? suggestion.Problem!
                : pick.Quote is null ? "Priced, but the chosen vehicle has no rate."
                : p.Quote.Total > pick.Quote.Total ? $"Costs ₹{p.Quote.Total - pick.Quote.Total:N2} more."
                : $"Costs the same or less but ranks lower ({(p.Type.VehicleTypeId == t.PreferredVehicleTypeId ? "tie-break" : "tie")}).";
            return new PlanAlternative(
                FreightMode.Ftl, p.Type.VehicleTypeId, p.Type.Name, p.Quote?.ContractId, p.Quote?.ContractReference, p.Quote?.TransporterId, p.Quote?.TransporterName,
                p.Quote?.Total, p.Quote?.Lines.Select(l => new PlanQuoteLine(l.Code, l.Description, l.Amount)).ToList() ?? [], isChosen, verdict,
                Transporter: p.Quote is null ? null : contacts.GetValueOrDefault(p.Quote.TransporterId), Vehicle: suggestion?.Vehicle, Driver: suggestion?.Driver);
        }).ToList();
        alternatives.AddRange(sizing.Where(s => !s.Accepted).Select(s =>
            new PlanAlternative(FreightMode.Ftl, s.VehicleTypeId, s.Name, null, null, null, null, null, [], false, s.Reason!)));

        double driving = 0;
        var at = 0;
        foreach (var index in chosen.Order)
        {
            driving += matrix.Minutes[at, index + 1];
            at = index + 1;
        }

        driving += matrix.Minutes[at, 0];

        var stopPlans = new List<MilkRunStopPlan>();
        foreach (var timing in chosen.Timings)
        {
            var g = group[timing.StopIndex];
            stopPlans.Add(new MilkRunStopPlan(
                stopPlans.Count + 1, g.Inbound ? "Pickup" : "Delivery", g.Def.Label,
                g.Orders.Select(o => new MilkRunOrderLine(o.OrderId, o.Number, o.WeightKg, o.VolumeCbm)).ToList(), g.WeightKg, g.VolumeCbm,
                timing.Arrival, timing.Departure, Math.Round(timing.WaitMinutes), timing.OnboardWeightKg));
        }

        var returnAt = chosen.DepotArrival ?? departure;
        stopPlans.Add(new MilkRunStopPlan(stopPlans.Count + 1, "Depot", t.DepotLabel, [], 0, null, returnAt, null, null, 0m));

        var cost = pick.Quote?.Total;
        var pickFleet = SuggestionOf(pick);
        if (pickFleet?.Problem is null && pickFleet is not null && pickFleet.Driver is null)
        {
            warnings.Add("No driver could be suggested for this trip.");
        }
        var tonnes = (inbound + outbound) / 1000m;
        var fill = pick.Type.WeightUtilisation;
        var reason = $"{pick.Type.Name} chosen for a peak load of {peakWeight:N0} kg ({pick.Type.WeightUtilisation:P0} of its payload)"
            + (cost is { } c ? $" at ₹{c:N2}" : string.Empty)
            + (t.PreferredVehicleTypeId is { } usual && usual != pick.Type.VehicleTypeId
                ? $". The usual vehicle was not used: {(alternatives.FirstOrDefault(a => a.VehicleTypeId == usual)?.Verdict ?? "it is not available").TrimEnd('.')}"
                : string.Empty)
            + ".";

        var trip = new MilkRunTrip(
            0, pick.Type.VehicleTypeId, pick.Type.Name, pick.Type.PayloadKg, pick.Type.VolumeCbm, pick.Quote?.ContractId, pick.Quote?.ContractReference,
            pick.Quote?.TransporterId, pick.Quote?.TransporterName, cost, pick.Quote?.Lines.Select(l => new PlanQuoteLine(l.Code, l.Description, l.Amount)).ToList() ?? [],
            km, Math.Round(driving), Math.Round(chosen.TotalMinutes), departure, returnAt, peakWeight, peakVolume, fill, pick.Type.VolumeUtilisation,
            cost is { } cc && km > 0 && tonnes > 0 ? Math.Round(cc / ((decimal)km * tonnes), 2) : null,
            stopPlans, group.Count > 1 ? chosen.Method : "Exact", planned.Feasible ? Math.Round(planned.TotalKm, 1) : null, shortest.Feasible ? Math.Round(shortest.TotalKm, 1) : null,
            alternatives, warnings, reason, matrix.Source,
            pick.Quote is null ? null : contacts.GetValueOrDefault(pick.Quote.TransporterId), pickFleet?.Vehicle, pickFleet?.Driver);
        return new TripResult(trip, null, null);
    }

    private static MilkRunPlan Build(
        MilkRunDefinition t, DateOnly date, List<MilkRunTrip> trips, List<MilkRunSkippedStop> skipped, IReadOnlyList<UnplannedOrder> unplanned, List<string> warnings)
    {
        var lines = trips.SelectMany(x => x.Stops).SelectMany(s => s.Orders.Select(o => (Stop: s, Order: o))).ToList();
        var priced = trips.All(x => x.Cost.HasValue);
        var totalCost = priced && trips.Count > 0 ? trips.Sum(x => x.Cost!.Value) : (decimal?)null;
        var km = trips.Sum(x => x.DistanceKm);
        var inbound = lines.Where(l => l.Stop.Kind == "Pickup").Sum(l => l.Order.WeightKg);
        var outbound = lines.Where(l => l.Stop.Kind == "Delivery").Sum(l => l.Order.WeightKg);
        var tonneKm = trips.Sum(x => (decimal)x.DistanceKm * (x.Stops.Where(s => s.Kind != "Depot").Sum(s => s.WeightKg)) / 1000m);
        warnings.AddRange(trips.SelectMany(x => x.Warnings.Select(w => $"Trip {x.Number}: {w}")));
        var totals = new MilkRunTotals(
            lines.Count, trips.Sum(x => x.Stops.Count(s => s.Kind != "Depot")), skipped.Count, trips.Count, inbound, outbound, Math.Round(km, 1), totalCost,
            totalCost is { } c && tonneKm > 0 ? Math.Round(c / tonneKm, 2) : null);
        return new MilkRunPlan(t.Code, t.Name, date, trips, skipped, unplanned, totals, trips.Count > 0 ? trips[0].Source : null, warnings);
    }

    private static UnplannedOrder Unplanned(MilkRunOrder o, string code, string reason) =>
        new(o.OrderId, o.Number, code, reason, RuleBasedPlanningOptimizer.SuggestionsFor(code));
}
