namespace Tms.Modules.Tracking.Domain;

/// <param name="StartToleranceMinutes">Starting tracking up to this long after the planned start still counts as on time.</param>
/// <param name="MinCoveragePct">A trip tracked for less of its time than this did not "keep tracking active".</param>
/// <param name="RepeatedGapCount">This many gaps on one trip is a pattern, not a one-off.</param>
public sealed record ComplianceSetting(int StartToleranceMinutes = 30, double MinCoveragePct = 90, int RepeatedGapCount = 3);

/// <summary>One trip's tracking, reduced to what compliance needs.</summary>
/// <param name="StoppedProperly">True when the driver ended tracking by completing the trip, false when it was abandoned, null while it is still running.</param>
public sealed record TripTracking(
    string Key, string Name, DateTimeOffset? PlannedStartAt, DateTimeOffset? TrackingStartedAt, int ExpectedMinutes, IReadOnlyList<int> GapMinutes, bool? StoppedProperly);

/// <param name="StaleTrips">Trips with at least one gap past the stale limit but never the lost limit.</param>
/// <param name="LostTrips">Trips with at least one gap past the lost limit.</param>
/// <param name="StartedOnTime">Of the trips that had a planned start; null when none did (not measurable, never zero).</param>
public sealed record ComplianceRow(
    string Key, string Name, int Trips, int ExpectedMinutes, int ActualMinutes, double? CoveragePct, int Gaps, int StaleTrips, int LostTrips,
    double? StartedOnTime, double? KeptActive, double? StoppedProperly, int TripsWithRepeatedGaps);

/// <summary>
/// Tracking coverage and driver tracking behaviour, as plain rates. These are operational figures: nothing here penalises a transporter, and a rate that cannot be measured is
/// reported as null rather than zero.
/// </summary>
public static class ComplianceCalculator
{
    public static ComplianceRow Row(string key, string name, IReadOnlyList<TripTracking> trips, ComplianceSetting rules, HealthSetting health)
    {
        var expected = trips.Sum(t => t.ExpectedMinutes);
        var gapMinutes = trips.Sum(t => Math.Min(t.ExpectedMinutes, t.GapMinutes.Sum()));
        var actual = Math.Max(0, expected - gapMinutes);

        double? Rate(int good, int of) => of == 0 ? null : Math.Round(good * 100.0 / of, 1);
        double Coverage(TripTracking t) => t.ExpectedMinutes <= 0 ? 100 : Math.Clamp((t.ExpectedMinutes - t.GapMinutes.Sum()) * 100.0 / t.ExpectedMinutes, 0, 100);

        var planned = trips.Where(t => t.PlannedStartAt is not null && t.TrackingStartedAt is not null).ToList();
        var finished = trips.Where(t => t.StoppedProperly is not null).ToList();
        return new ComplianceRow(
            key, name, trips.Count, expected, actual, expected == 0 ? null : Math.Round(actual * 100.0 / expected, 1),
            trips.Sum(t => t.GapMinutes.Count),
            trips.Count(t => t.GapMinutes.Any(g => g >= health.StaleAfterMinutes) && !t.GapMinutes.Any(g => g >= health.LostAfterMinutes)),
            trips.Count(t => t.GapMinutes.Any(g => g >= health.LostAfterMinutes)),
            Rate(planned.Count(t => t.TrackingStartedAt!.Value <= t.PlannedStartAt!.Value.AddMinutes(rules.StartToleranceMinutes)), planned.Count),
            Rate(trips.Count(t => Coverage(t) >= rules.MinCoveragePct), trips.Count),
            Rate(finished.Count(t => t.StoppedProperly == true), finished.Count),
            trips.Count(t => t.GapMinutes.Count >= rules.RepeatedGapCount));
    }
}
