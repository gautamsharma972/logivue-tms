namespace Tms.Modules.Shipments.Domain;

public enum StopRole
{
    /// <summary>Unloads goods the truck is carrying: onboard weight goes down.</summary>
    Delivery = 1,

    /// <summary>Loads a return: onboard weight goes up, so capacity must still hold at that moment.</summary>
    ReturnPickup = 2,
}

/// <param name="Deadline">Latest acceptable arrival. For a <see cref="StopRole.ReturnPickup"/> it applies to the return to the depot.</param>
/// <param name="ServiceMinutes">Time spent at this stop; falls back to the problem's default.</param>
/// <param name="WindowFrom">Earliest time of day the stop accepts the truck (it waits if early; if it arrives after <paramref name="WindowTo"/> it waits until the next day).</param>
public sealed record SequenceStop(
    string Id, StopRole Role, decimal WeightKg, decimal? VolumeCbm, DateTimeOffset? Deadline, TimeOnly? WindowFrom, TimeOnly? WindowTo, int? ServiceMinutes = null);

/// <param name="Matrix">Index 0 is the depot; index i + 1 is <c>Stops[i]</c>.</param>
public sealed record SequenceProblem(
    DistanceMatrix Matrix,
    IReadOnlyList<SequenceStop> Stops,
    DateTimeOffset Departure,
    int ServiceMinutes,
    bool ReturnToDepot,
    decimal InitialWeightKg,
    decimal? InitialVolumeCbm,
    decimal? PayloadKg,
    decimal? VolumeCapacityCbm,
    bool ReturnsAfterDeliveries = false);

public sealed record StopTiming(int StopIndex, DateTimeOffset Arrival, DateTimeOffset Departure, double WaitMinutes, decimal OnboardWeightKg);

/// <param name="Order">Indices into <see cref="SequenceProblem.Stops"/> in visiting order.</param>
/// <param name="Method"><c>Exact</c> when every order was considered, <c>Heuristic</c> for local search on larger routes.</param>
public sealed record SequenceResult(
    IReadOnlyList<int> Order,
    double TotalKm,
    double TotalMinutes,
    IReadOnlyList<StopTiming> Timings,
    DateTimeOffset? DepotArrival,
    bool Feasible,
    string? Violation,
    string Method);

/// <summary>
/// Chooses the visiting order of a truck's stops: shortest total distance that keeps the vehicle within capacity at every
/// moment and meets delivery windows and deadlines. Exact (branch and bound) up to <see cref="ExactLimit"/> stops, then nearest
/// neighbour plus 2-opt and relocate local search. It reports honestly which one it used.
/// </summary>
public static class StopSequencer
{
    public const int ExactLimit = 8;

    private const int MaxImprovementPasses = 100;

    public static SequenceResult Solve(SequenceProblem problem)
    {
        var n = problem.Stops.Count;
        if (n == 0)
        {
            return Finish(problem, [], "Exact");
        }

        return n <= ExactLimit ? SolveExact(problem) : SolveHeuristic(problem);
    }

    /// <summary>Evaluates a given order (used for manual re-ordering): timings, feasibility and the first violation, if any.</summary>
    public static SequenceResult Evaluate(SequenceProblem problem, IReadOnlyList<int> order) => Finish(problem, order, "Given");

    private static SequenceResult SolveExact(SequenceProblem p)
    {
        var n = p.Stops.Count;
        List<int>? best = null;
        double bestKm = double.MaxValue, bestMinutes = double.MaxValue;
        var prefix = new List<int>();
        var used = new bool[n];

        void Search()
        {
            if (prefix.Count == n)
            {
                var sim = Simulate(p, prefix);
                if (sim.Violation is null && (sim.Km < bestKm - 1e-9 || (Math.Abs(sim.Km - bestKm) < 1e-9 && sim.Minutes < bestMinutes)))
                {
                    best = [.. prefix];
                    bestKm = sim.Km;
                    bestMinutes = sim.Minutes;
                }

                return;
            }

            for (var i = 0; i < n; i++)
            {
                if (used[i])
                {
                    continue;
                }

                used[i] = true;
                prefix.Add(i);
                var sim = Simulate(p, prefix, partial: true);
                if (sim.Violation is null && sim.Km < bestKm)
                {
                    Search();
                }

                prefix.RemoveAt(prefix.Count - 1);
                used[i] = false;
            }
        }

        Search();
        return best is not null ? Finish(p, best, "Exact") : Finish(p, Enumerable.Range(0, n).ToList(), "Exact"); // none feasible: report why for the given order
    }

    private static SequenceResult SolveHeuristic(SequenceProblem p)
    {
        var n = p.Stops.Count;
        var current = NearestNeighbour(p);
        var currentSim = Simulate(p, current);

        // If construction left it infeasible, still try to improve: moves are accepted when they reduce the violation count or distance.
        for (var pass = 0; pass < MaxImprovementPasses; pass++)
        {
            var improved = false;

            for (var i = 0; i < n - 1 && !improved; i++)
            {
                for (var j = i + 1; j < n && !improved; j++)
                {
                    var candidate = current.Take(i).Concat(current.Skip(i).Take(j - i + 1).Reverse()).Concat(current.Skip(j + 1)).ToList();
                    improved = TryAccept(p, candidate, ref current, ref currentSim);
                }
            }

            for (var from = 0; from < n && !improved; from++)
            {
                for (var to = 0; to < n && !improved; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    var candidate = new List<int>(current);
                    var item = candidate[from];
                    candidate.RemoveAt(from);
                    candidate.Insert(to, item);
                    improved = TryAccept(p, candidate, ref current, ref currentSim);
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return Finish(p, current, "Heuristic");
    }

    private static bool TryAccept(SequenceProblem p, List<int> candidate, ref List<int> current, ref Sim currentSim)
    {
        var sim = Simulate(p, candidate);
        // An infeasible route is replaced by any feasible one; a feasible route only by a shorter feasible one.
        var better = sim.Violation is null && (currentSim.Violation is not null || sim.Km < currentSim.Km - 1e-9);
        if (!better)
        {
            return false;
        }

        current = candidate;
        currentSim = sim;
        return true;
    }

    /// <summary>Greedy: always go to the nearest stop that keeps the route feasible so far; falls back to input order if it dead-ends.</summary>
    private static List<int> NearestNeighbour(SequenceProblem p)
    {
        var n = p.Stops.Count;
        var order = new List<int>();
        var left = Enumerable.Range(0, n).ToList();
        var at = 0;
        while (left.Count > 0)
        {
            var choice = left
                .Where(i => Simulate(p, [.. order, i], partial: true).Violation is null)
                .OrderBy(i => p.Matrix.Km[at, i + 1])
                .Cast<int?>()
                .FirstOrDefault();
            if (choice is null)
            {
                return Enumerable.Range(0, n).ToList();
            }

            order.Add(choice.Value);
            left.Remove(choice.Value);
            at = choice.Value + 1;
        }

        return order;
    }

    private readonly record struct Sim(double Km, double Minutes, string? Violation);

    /// <summary>Walks the order, applying travel, windows, service and load changes. <paramref name="partial"/> skips the depot return and return deadlines.</summary>
    private static Sim Simulate(SequenceProblem p, IReadOnlyList<int> order, bool partial = false) =>
        Walk(p, order, partial, timings: null, out _);

    private static Sim Walk(SequenceProblem p, IReadOnlyList<int> order, bool partial, List<StopTiming>? timings, out DateTimeOffset? depotArrival)
    {
        depotArrival = null;
        var clock = p.Departure;
        var load = p.InitialWeightKg;
        var volume = p.InitialVolumeCbm ?? 0m;
        double km = 0, minutes = 0;
        var at = 0;
        var collectedReturn = false;

        if (p.PayloadKg is { } payload0 && load > payload0)
        {
            return new Sim(0, 0, $"The vehicle starts overloaded: {load:0.##} kg against {payload0:0.##} kg.");
        }

        foreach (var index in order)
        {
            var stop = p.Stops[index];
            if (p.ReturnsAfterDeliveries && stop.Role == StopRole.Delivery && collectedReturn)
            {
                return new Sim(km, minutes, $"{stop.Id} would be delivered after a return pickup, but deliveries must finish before returns are collected.");
            }

            collectedReturn |= stop.Role == StopRole.ReturnPickup;
            var to = index + 1;
            var travel = p.Matrix.Minutes[at, to];
            km += p.Matrix.Km[at, to];
            minutes += travel;
            clock = clock.AddMinutes(travel);

            var arrival = clock;
            var start = ApplyWindow(arrival, stop.WindowFrom, stop.WindowTo);
            var wait = (start - arrival).TotalMinutes;
            minutes += wait;

            if (stop.Role == StopRole.Delivery && stop.Deadline is { } deadline && start > deadline)
            {
                return new Sim(km, minutes, $"{stop.Id} would be reached {start:dd MMM HH:mm}, after its deadline {deadline:dd MMM HH:mm}.");
            }

            var service = stop.ServiceMinutes ?? p.ServiceMinutes;
            clock = start.AddMinutes(service);
            minutes += service;

            if (stop.Role == StopRole.Delivery)
            {
                load -= stop.WeightKg;
                volume -= stop.VolumeCbm ?? 0m;
            }
            else
            {
                load += stop.WeightKg;
                volume += stop.VolumeCbm ?? 0m;
                if (p.PayloadKg is { } payload && load > payload)
                {
                    return new Sim(km, minutes, $"Picking up {stop.Id} would put {load:0.##} kg on a vehicle that carries {payload:0.##} kg (over by {load - payload:0.##} kg).");
                }

                if (p.VolumeCapacityCbm is { } cap && p.InitialVolumeCbm is not null && volume > cap)
                {
                    return new Sim(km, minutes, $"Picking up {stop.Id} would put {volume:0.##} CBM on a vehicle with {cap:0.##} CBM (over by {volume - cap:0.##} CBM).");
                }
            }

            timings?.Add(new StopTiming(index, arrival, clock, wait, load));
            at = to;
        }

        if (p.ReturnToDepot && !partial && order.Count > 0)
        {
            km += p.Matrix.Km[at, 0];
            minutes += p.Matrix.Minutes[at, 0];
            clock = clock.AddMinutes(p.Matrix.Minutes[at, 0]);
            depotArrival = clock;

            foreach (var i in order)
            {
                var stop = p.Stops[i];
                if (stop.Role == StopRole.ReturnPickup && stop.Deadline is { } deadline && clock > deadline)
                {
                    return new Sim(km, minutes, $"The return {stop.Id} would reach the depot {clock:dd MMM HH:mm}, after its deadline {deadline:dd MMM HH:mm}.");
                }
            }
        }

        return new Sim(km, minutes, null);
    }

    private static SequenceResult Finish(SequenceProblem p, IReadOnlyList<int> order, string method)
    {
        var timings = new List<StopTiming>();
        var sim = Walk(p, order, partial: false, timings, out var depotArrival);
        return new SequenceResult(order, Math.Round(sim.Km, 2), Math.Round(sim.Minutes, 1), timings, depotArrival, sim.Violation is null, sim.Violation, method);
    }

    /// <summary>The time service can start: waits for the window to open, or until the next day if the window has closed.</summary>
    public static DateTimeOffset ApplyWindow(DateTimeOffset arrival, TimeOnly? from, TimeOnly? to)
    {
        if (from is null && to is null)
        {
            return arrival;
        }

        var open = from ?? TimeOnly.MinValue;
        var close = to ?? TimeOnly.MaxValue;
        var time = TimeOnly.FromTimeSpan(arrival.TimeOfDay);
        var day = new DateTimeOffset(arrival.Year, arrival.Month, arrival.Day, open.Hour, open.Minute, 0, arrival.Offset);
        return time < open ? day : time > close ? day.AddDays(1) : arrival;
    }
}
