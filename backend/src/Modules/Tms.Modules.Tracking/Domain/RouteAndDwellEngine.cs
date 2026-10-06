namespace Tms.Modules.Tracking.Domain;

public sealed class DeviationState
{
    public DateTimeOffset? OffRouteSince { get; set; }

    public int OffPoints { get; set; }

    public double MaxOffKm { get; set; }

    public bool IsOpen { get; set; }

    public DateTimeOffset? BackSince { get; set; }
}

public enum DeviationChange
{
    None = 0,
    Opened = 1,
    Updated = 2,
    Resolved = 3,
}

public sealed record DeviationResult(DeviationChange Change, Severity Severity, double MaxOffKm, int DurationMinutes, DateTimeOffset? StartedAt);

/// <summary>
/// A deviation is the vehicle being outside the planned corridor for long enough to matter, never a single point and never a short detour. GPS vagueness is taken off the
/// distance first, and it is closed only after the vehicle has been back on the corridor for a while.
/// </summary>
public static class DeviationTracker
{
    public static DeviationResult Observe(DeviationState state, double offRouteKm, double? accuracyM, DateTimeOffset at, RouteSetting rules)
    {
        var effective = Math.Max(0, offRouteKm - (accuracyM ?? 0) / 1000.0);
        if (effective > rules.DeviationKm)
        {
            state.BackSince = null;
            state.OffRouteSince ??= at;
            state.OffPoints++;
            state.MaxOffKm = Math.Max(state.MaxOffKm, effective);
            var minutes = (int)(at - state.OffRouteSince.Value).TotalMinutes;
            if (!state.IsOpen)
            {
                if (state.OffPoints >= rules.MinOffRoutePoints && minutes >= rules.DeviationMinutes)
                {
                    state.IsOpen = true;
                    return new DeviationResult(DeviationChange.Opened, SeverityFor(state.MaxOffKm, minutes, rules), state.MaxOffKm, minutes, state.OffRouteSince);
                }

                return new DeviationResult(DeviationChange.None, Severity.Informational, state.MaxOffKm, minutes, state.OffRouteSince);
            }

            return new DeviationResult(DeviationChange.Updated, SeverityFor(state.MaxOffKm, minutes, rules), state.MaxOffKm, minutes, state.OffRouteSince);
        }

        if (!state.IsOpen)
        {
            state.OffRouteSince = null;
            state.OffPoints = 0;
            state.MaxOffKm = 0;
            return new DeviationResult(DeviationChange.None, Severity.Informational, 0, 0, null);
        }

        state.BackSince ??= at;
        if ((at - state.BackSince.Value).TotalMinutes < rules.ResolveMinutes)
        {
            return new DeviationResult(DeviationChange.None, Severity.Informational, state.MaxOffKm, 0, state.OffRouteSince);
        }

        var started = state.OffRouteSince;
        var duration = (int)(state.BackSince.Value - (started ?? state.BackSince.Value)).TotalMinutes;
        var maxOff = state.MaxOffKm;
        var severity = SeverityFor(maxOff, duration, rules);
        state.IsOpen = false;
        state.OffRouteSince = null;
        state.OffPoints = 0;
        state.MaxOffKm = 0;
        state.BackSince = null;
        return new DeviationResult(DeviationChange.Resolved, severity, maxOff, duration, started);
    }

    public static Severity SeverityFor(double maxOffKm, int minutes, RouteSetting rules) =>
        maxOffKm >= rules.CriticalKm || minutes >= rules.CriticalMinutes ? Severity.Critical : maxOffKm >= rules.HighKm ? Severity.High : Severity.Warning;
}

public sealed class DwellState
{
    public GeoPoint? Anchor { get; set; }

    public DateTimeOffset? Since { get; set; }

    public DateTimeOffset? LastStationaryAt { get; set; }

    public bool Open { get; set; }

    public bool PlannedStop { get; set; }

    public string? Where { get; set; }

    public bool ExcessRaised { get; set; }

    public bool UnplannedRaised { get; set; }
}

public enum DwellChange
{
    None = 0,
    Started = 1,
    Ended = 2,
    Excess = 3,
    UnplannedStop = 4,
}

public sealed record DwellResult(DwellChange Change, DwellKind Kind, string? Where, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, int DurationMinutes, int ExpectedMinutes, int ExcessMinutes);

/// <summary>
/// A vehicle that has not left a small area for a while is dwelling. At a known place that is the expected business of a stop and only an unusually long one is a problem;
/// anywhere else it is an unplanned stop. Both limits come from settings, by kind of place.
/// </summary>
public static class DwellTracker
{
    public static IReadOnlyList<DwellResult> Observe(DwellState state, GeoPoint point, double? speedKph, DateTimeOffset at, string? knownPlaceKind, string? knownPlaceName, DwellSetting rules)
    {
        var results = new List<DwellResult>();
        if (state.Anchor is not { } anchor)
        {
            Reset(state, point, at);
            return results;
        }

        var moved = Geo.DistanceMetres(anchor, point) > rules.StationaryRadiusM || speedKph is { } s && s >= Math.Max(rules.StationarySpeedKph * 5, 15);
        if (moved)
        {
            if (state.Open)
            {
                var end = state.LastStationaryAt ?? at;
                var duration = (int)(end - state.Since!.Value).TotalMinutes;
                results.Add(new DwellResult(DwellChange.Ended, state.PlannedStop ? DwellKind.PlannedStop : DwellKind.UnplannedStop, state.Where, state.Since, end, duration,
                    state.PlannedStop ? rules.Expected(state.Where ?? "Default") : 0, 0));
            }

            Reset(state, point, at);
            return results;
        }

        state.LastStationaryAt = at;
        var minutes = (int)(at - state.Since!.Value).TotalMinutes;
        if (!state.Open && minutes >= rules.MinStationaryMinutes)
        {
            state.Open = true;
            state.PlannedStop = knownPlaceKind is not null;
            state.Where = knownPlaceKind is not null ? knownPlaceKind : null;
            results.Add(new DwellResult(DwellChange.Started, state.PlannedStop ? DwellKind.PlannedStop : DwellKind.UnplannedStop, knownPlaceName ?? state.Where, state.Since, null, minutes, 0, 0));
        }

        if (state.Open && state.PlannedStop && !state.ExcessRaised)
        {
            var expected = rules.Expected(state.Where ?? "Default");
            if (minutes - expected >= rules.ExcessThresholdMinutes)
            {
                state.ExcessRaised = true;
                results.Add(new DwellResult(DwellChange.Excess, DwellKind.PlannedStop, knownPlaceName ?? state.Where, state.Since, null, minutes, expected, minutes - expected));
            }
        }

        if (state.Open && !state.PlannedStop && !state.UnplannedRaised && minutes >= rules.UnplannedStopMinutes)
        {
            state.UnplannedRaised = true;
            results.Add(new DwellResult(DwellChange.UnplannedStop, DwellKind.UnplannedStop, null, state.Since, null, minutes, 0, minutes));
        }

        return results;
    }

    /// <summary>The stop is over because the trip is: whatever dwell is open is closed at the last time the vehicle was seen still.</summary>
    public static DwellResult? Close(DwellState state, DwellSetting rules)
    {
        if (!state.Open || state.Since is not { } since)
        {
            return null;
        }

        var end = state.LastStationaryAt ?? since;
        var result = new DwellResult(DwellChange.Ended, state.PlannedStop ? DwellKind.PlannedStop : DwellKind.UnplannedStop, state.Where, since, end, (int)(end - since).TotalMinutes,
            state.PlannedStop ? rules.Expected(state.Where ?? "Default") : 0, 0);
        state.Open = false;
        return result;
    }

    private static void Reset(DwellState state, GeoPoint point, DateTimeOffset at)
    {
        state.Anchor = point;
        state.Since = at;
        state.LastStationaryAt = at;
        state.Open = false;
        state.PlannedStop = false;
        state.Where = null;
        state.ExcessRaised = false;
        state.UnplannedRaised = false;
    }
}
