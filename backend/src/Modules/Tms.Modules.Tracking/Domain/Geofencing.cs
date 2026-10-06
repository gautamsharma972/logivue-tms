namespace Tms.Modules.Tracking.Domain;

/// <summary>A place, as a circle (centre and radius) or, when a polygon is given, a polygon.</summary>
public sealed record GeofenceShape(GeoPoint Center, double RadiusM, IReadOnlyList<GeoPoint>? Polygon = null)
{
    public bool Contains(GeoPoint point) =>
        Polygon is { Count: >= 3 } ring ? Geo.InPolygon(point, ring) : Geo.DistanceMetres(Center, point) <= RadiusM;

    /// <summary>How far, in metres, the point is from the edge (positive either side).</summary>
    public double DistanceToEdgeM(GeoPoint point) =>
        Polygon is { Count: >= 3 } ring ? Geo.DistanceToPolygonEdgeKm(point, ring) * 1000 : Math.Abs(Geo.DistanceMetres(Center, point) - RadiusM);
}

public enum Presence
{
    Outside = 1,
    EntryCandidate = 2,
    Inside = 3,
    ExitCandidate = 4,
}

/// <summary>Where a vehicle stands in relation to one geofence, with enough memory to confirm a change before announcing it.</summary>
public sealed class PresenceState
{
    public Presence State { get; set; } = Presence.Outside;

    public DateTimeOffset? Since { get; set; }

    public int Points { get; set; }

    public DateTimeOffset? InsideSince { get; set; }

    public bool StayedRaised { get; set; }
}

public sealed record GeofenceTransition(GeofenceEventType Type, DateTimeOffset At, double Confidence);

/// <summary>
/// Entering or leaving a place is announced only after it has been confirmed, so one noisy fix cannot do it. A vague fix is ignored, and a change must hold over a minimum number
/// of good fixes and a minimum time. The announced time is that of the first fix of the change, not of the fix that confirmed it.
/// </summary>
public static class GeofenceEvaluator
{
    public static GeofenceTransition? Observe(PresenceState state, GeofenceShape shape, GeoPoint point, double? accuracyM, DateTimeOffset at, GeofenceSetting rules)
    {
        if (accuracyM is { } accuracy && accuracy > rules.MaxAccuracyM)
        {
            return null;
        }

        var inside = shape.Contains(point);
        var confidence = accuracyM is { } a ? Math.Round(Math.Clamp(1 - a / Math.Max(rules.MaxAccuracyM, 1) * 0.5, 0.4, 1), 2) : 0.8;
        switch (state.State)
        {
            case Presence.Outside:
                if (inside)
                {
                    state.State = Presence.EntryCandidate;
                    state.Since = at;
                    state.Points = 1;
                    return Confirmed(state, rules, at, confidence, entering: true);
                }

                return null;

            case Presence.EntryCandidate:
                if (!inside)
                {
                    state.State = Presence.Outside;
                    state.Since = null;
                    state.Points = 0;
                    return null;
                }

                state.Points++;
                return Confirmed(state, rules, at, confidence, entering: true);

            case Presence.Inside:
                if (!inside)
                {
                    state.State = Presence.ExitCandidate;
                    state.Since = at;
                    state.Points = 1;
                    return Confirmed(state, rules, at, confidence, entering: false);
                }

                if (!state.StayedRaised && state.InsideSince is { } entered && (at - entered).TotalMinutes >= rules.StayedAfterMinutes)
                {
                    state.StayedRaised = true;
                    return new GeofenceTransition(GeofenceEventType.Stayed, at, confidence);
                }

                return null;

            default: // ExitCandidate
                if (inside)
                {
                    state.State = Presence.Inside; // it was noise: the vehicle never left
                    state.Since = null;
                    state.Points = 0;
                    return null;
                }

                state.Points++;
                return Confirmed(state, rules, at, confidence, entering: false);
        }
    }

    /// <summary>The vehicle's trip is ending: a change that has been seen but not yet confirmed is taken as real rather than lost.</summary>
    public static GeofenceTransition? Flush(PresenceState state, DateTimeOffset at)
    {
        if (state.State == Presence.EntryCandidate && state.Since is { } since)
        {
            state.State = Presence.Inside;
            state.InsideSince = since;
            return new GeofenceTransition(GeofenceEventType.Entered, since, 0.6);
        }

        return null;
    }

    private static GeofenceTransition? Confirmed(PresenceState state, GeofenceSetting rules, DateTimeOffset at, double confidence, bool entering)
    {
        var seconds = entering ? rules.EntryConfirmationSeconds : rules.ExitConfirmationSeconds;
        if (state.Points < rules.MinConfirmationPoints || state.Since is not { } since || (at - since).TotalSeconds < seconds)
        {
            return null;
        }

        if (entering)
        {
            state.State = Presence.Inside;
            state.InsideSince = since;
            state.StayedRaised = false;
            return new GeofenceTransition(GeofenceEventType.Entered, since, confidence);
        }

        state.State = Presence.Outside;
        state.InsideSince = null;
        state.StayedRaised = false;
        return new GeofenceTransition(GeofenceEventType.Exited, since, confidence);
    }
}
