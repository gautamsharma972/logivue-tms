namespace Tms.Modules.Tracking.Domain;

/// <summary>A stop the estimate has to reach. <paramref name="AlongKm"/> is where it lies on the route, when there is a route.</summary>
public sealed record EtaStop(
    Guid StopId, int Sequence, StopKind Kind, string Name, GeoPoint? Point, double? AlongKm, DateTimeOffset? Planned, DateTimeOffset? WindowEnd, int ExpectedDwellMinutes, StopStatus Status,
    DateTimeOffset? ArrivedAt);

public sealed record EtaInput(
    DateTimeOffset Now, GeoPoint Position, double? VehicleAlongKm, bool HasRoute, bool RouteIsEstimate, double? RecentMovingSpeedKph, int SpeedSamples, IReadOnlyList<EtaStop> Stops,
    TrackingHealth Health, bool OffRoute);

public sealed record StopEta(Guid StopId, DateTimeOffset Eta, double RemainingKm, double Confidence, RiskStatus Risk, RiskLevel Level, int DelayMinutes);

/// <summary>
/// A deterministic, rule-based arrival estimate: distance left at a blend of the speed actually seen and a typical average, plus the stay expected at each stop on the way.
/// It makes no claim to machine-learning precision; the confidence says how much the inputs can be trusted and is capped well below certainty.
/// </summary>
public static class EtaCalculator
{
    private const double Circuity = 1.3;

    public static IReadOnlyList<StopEta> Calculate(EtaInput input, EtaSetting rules)
    {
        var speed = Speed(input, rules);
        var results = new List<StopEta>();
        var cursorTime = input.Now;
        var cursorAlong = input.VehicleAlongKm;
        var cursorPoint = input.Position;

        foreach (var stop in input.Stops.Where(s => s.Status != StopStatus.Departed && s.Status != StopStatus.Skipped).OrderBy(s => s.Sequence))
        {
            // A stop the vehicle has already driven past without it ever being seen there (a gap in the signal, a yard with no coverage) is behind it: it adds no distance and no stay.
            if (stop.Status is StopStatus.Pending or StopStatus.Approaching && input.HasRoute && stop.AlongKm is { } alongStop && input.VehicleAlongKm is { } vehicleNow && alongStop < vehicleNow - 0.5)
            {
                continue;
            }

            double remainingKm;
            DateTimeOffset arrival;
            if (stop.Status == StopStatus.Arrived && stop.ArrivedAt is { } arrived)
            {
                arrival = arrived;
                remainingKm = 0;
            }
            else
            {
                remainingKm = input.HasRoute && stop.AlongKm is { } stopAlong && cursorAlong is { } vehicleAlong
                    ? Math.Max(0, stopAlong - vehicleAlong)
                    : stop.Point is { } p ? Geo.DistanceKm(cursorPoint, p) * Circuity : 0;
                arrival = cursorTime.AddMinutes(remainingKm / speed * 60);
            }

            var confidence = Confidence(input, results.Count, remainingKm);
            var basis = stop.WindowEnd ?? stop.Planned;
            var delay = basis is { } b ? (int)Math.Round((arrival - b).TotalMinutes) - rules.SlaBufferMinutes : 0;
            if (stop.Status != StopStatus.Arrived && basis is { } deadline && input.Now > deadline)
            {
                delay = Math.Max(delay, (int)(input.Now - deadline).TotalMinutes - rules.SlaBufferMinutes); // already late, whatever the estimate says
            }

            var risk = basis is null ? RiskStatus.Unknown : Classify(delay, rules);
            results.Add(new StopEta(stop.StopId, arrival, Math.Round(remainingKm, 1), confidence, risk, LevelOf(risk), delay));

            // The stay at this stop pushes everything after it back: what is left of it if the vehicle is there now, all of it if not yet.
            var stays = stop.Status == StopStatus.Arrived && stop.ArrivedAt is { } at
                ? at.AddMinutes(Math.Max(stop.ExpectedDwellMinutes, (input.Now - at).TotalMinutes))
                : arrival.AddMinutes(stop.ExpectedDwellMinutes);
            if (stop.WindowEnd is null && stop.Planned is { } planned && stop.Kind == StopKind.Pickup && arrival < planned)
            {
                stays = planned.AddMinutes(stop.ExpectedDwellMinutes); // a pickup is not loaded before it is due
            }

            cursorTime = stays > cursorTime ? stays : cursorTime;
            cursorAlong = stop.AlongKm ?? cursorAlong;
            cursorPoint = stop.Point ?? cursorPoint;
        }

        return results;
    }

    public static RiskStatus Classify(int delayMinutes, EtaSetting rules) =>
        delayMinutes <= rules.OnTimeToleranceMinutes ? RiskStatus.OnTime
        : delayMinutes <= rules.DelayedAfterMinutes ? RiskStatus.AtRisk
        : delayMinutes <= rules.SeverelyDelayedAfterMinutes ? RiskStatus.Delayed
        : RiskStatus.SeverelyDelayed;

    public static RiskLevel LevelOf(RiskStatus risk) => risk switch
    {
        RiskStatus.OnTime => RiskLevel.Low,
        RiskStatus.AtRisk => RiskLevel.Medium,
        RiskStatus.Delayed => RiskLevel.High,
        RiskStatus.SeverelyDelayed => RiskLevel.Critical,
        _ => RiskLevel.Low,
    };

    internal static double Speed(EtaInput input, EtaSetting rules)
    {
        var average = Math.Max(rules.AverageSpeedKph, 5);
        if (input.RecentMovingSpeedKph is not { } seen || input.SpeedSamples == 0)
        {
            return average;
        }

        var blend = Math.Clamp(rules.SpeedBlend, 0, 1);
        return Math.Clamp(Math.Clamp(seen, 15, 90) * blend + average * (1 - blend), 10, 90);
    }

    internal static double Confidence(EtaInput input, int stopsBefore, double remainingKm)
    {
        var confidence = 0.9;
        if (input.RouteIsEstimate || !input.HasRoute)
        {
            confidence -= 0.15;
        }

        confidence -= input.Health switch { TrackingHealth.Stale => 0.2, TrackingHealth.Lost => 0.4, _ => 0 };
        if (input.SpeedSamples < 3)
        {
            confidence -= 0.1;
        }

        if (input.OffRoute)
        {
            confidence -= 0.15;
        }

        if (remainingKm > 600)
        {
            confidence -= 0.05;
        }

        confidence -= Math.Min(0.1, stopsBefore * 0.02);
        return Math.Round(Math.Clamp(confidence, 0.2, 0.95), 2);
    }
}
