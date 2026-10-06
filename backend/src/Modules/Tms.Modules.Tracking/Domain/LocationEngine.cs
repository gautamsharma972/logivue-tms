namespace Tms.Modules.Tracking.Domain;

/// <summary>One GPS fix as it arrives from any source, before anything has been decided about it.</summary>
public sealed record LocationInput(
    double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKph, double? Heading, DateTimeOffset CapturedAt, bool MockLocation = false);

public sealed record PreviousFix(GeoPoint Point, DateTimeOffset CapturedAt, int IdenticalCount);

public sealed record LocationVerdict(LocationValidation Status, LocationAnomaly Anomalies, IReadOnlyList<string> Reasons)
{
    public bool IsUsable => Status == LocationValidation.Valid;
}

/// <summary>
/// Decides whether a GPS fix can be believed. A fix that cannot be real is refused, one that is doubtful is kept for the record but never moves the vehicle, fires a geofence or
/// changes an estimate, and nothing is silently accepted. All limits come from the tenant's settings.
/// </summary>
public static class LocationValidator
{
    public static LocationVerdict Validate(LocationInput fix, PreviousFix? previous, DateTimeOffset now, ValidationSetting rules)
    {
        var reasons = new List<string>();
        var anomalies = LocationAnomaly.None;

        if (!Geo.IsValid(fix.Latitude, fix.Longitude))
        {
            return Refused("The latitude or longitude is not a real position.", anomalies);
        }

        if (fix.Latitude == 0 && fix.Longitude == 0)
        {
            return Refused("The position 0, 0 is what a device reports when it has no fix.", anomalies);
        }

        if (fix.AccuracyMeters is { } accuracy)
        {
            if (accuracy < 0 || double.IsNaN(accuracy) || accuracy > rules.RejectAccuracyM)
            {
                return Refused($"An accuracy of {accuracy:0} m is not usable.", anomalies | LocationAnomaly.PoorAccuracy);
            }

            if (accuracy > rules.MaxAccuracyM)
            {
                anomalies |= LocationAnomaly.PoorAccuracy;
                reasons.Add($"The fix is only accurate to {accuracy:0} m (limit {rules.MaxAccuracyM} m).");
            }
        }

        if (fix.CapturedAt > now.AddMinutes(rules.FutureToleranceMinutes))
        {
            return Refused("The fix is stamped in the future.", anomalies | LocationAnomaly.FutureTimestamp);
        }

        if (fix.CapturedAt < now.AddDays(-rules.RejectOlderThanDays))
        {
            return Refused($"The fix is older than {rules.RejectOlderThanDays} days.", anomalies | LocationAnomaly.OldTimestamp);
        }

        if (fix.SpeedKph is { } speed && (speed < 0 || double.IsNaN(speed)))
        {
            return Refused("The reported speed is not a real speed.", anomalies);
        }

        if (fix.SpeedKph is { } reported && reported > rules.MaxSpeedKph)
        {
            anomalies |= LocationAnomaly.ImpossibleSpeed;
            reasons.Add($"A reported speed of {reported:0} km/h is not plausible for a truck.");
        }

        if (fix.MockLocation && rules.TreatMockLocationAsSuspicious)
        {
            anomalies |= LocationAnomaly.MockLocation;
            reasons.Add("The device says this position comes from a mock-location app.");
        }

        if (previous is not null && fix.CapturedAt > previous.CapturedAt)
        {
            var km = Geo.DistanceKm(previous.Point, new GeoPoint(fix.Latitude, fix.Longitude));
            var hours = (fix.CapturedAt - previous.CapturedAt).TotalHours;
            var implied = hours > 0 ? km / hours : 0;
            if (implied > rules.MaxImpliedSpeedKph && km > 0.5)
            {
                anomalies |= km > 50 ? LocationAnomaly.LargeJump : LocationAnomaly.ImpossibleSpeed;
                reasons.Add($"The vehicle would have moved {km:0.#} km in {hours * 60:0.#} minutes ({implied:0} km/h).");
            }

            var identical = Geo.DistanceKm(previous.Point, new GeoPoint(fix.Latitude, fix.Longitude)) < 0.0005;
            if (identical && previous.IdenticalCount + 1 >= rules.RepeatedIdenticalLimit && fix.SpeedKph is > 10)
            {
                anomalies |= LocationAnomaly.RepeatedCoordinates;
                reasons.Add($"The same position {previous.IdenticalCount + 1} times in a row while the device reports movement.");
            }

            if (fix.SpeedKph is > 25 && implied < 2 && hours >= 2.0 / 60)
            {
                anomalies |= LocationAnomaly.ReportedSpeedMismatch;
                reasons.Add($"The device reports {fix.SpeedKph:0} km/h but the position has hardly changed.");
            }
        }

        var suspicious = anomalies & ~(LocationAnomaly.ReportedSpeedMismatch | LocationAnomaly.DeviceTimeMismatch);
        return new LocationVerdict(suspicious == LocationAnomaly.None ? LocationValidation.Valid : LocationValidation.Suspicious, anomalies, reasons);
    }

    private static LocationVerdict Refused(string reason, LocationAnomaly anomalies) => new(LocationValidation.Rejected, anomalies, [reason]);
}

/// <summary>How well tracking itself is working. Age is measured from the GPS time of the last fix, never from when the server heard of it.</summary>
public static class HealthEvaluator
{
    public static (TrackingHealth Health, int AgeMinutes) Evaluate(TrackingSessionStatus status, DateTimeOffset? lastCapturedAt, DateTimeOffset startedAt, DateTimeOffset now, HealthSetting rules)
    {
        switch (status)
        {
            case TrackingSessionStatus.NotStarted:
                return (TrackingHealth.NotStarted, 0);
            case TrackingSessionStatus.Completed or TrackingSessionStatus.Cancelled:
                return (TrackingHealth.Completed, 0);
        }

        var age = (int)Math.Max(0, (now - (lastCapturedAt ?? startedAt)).TotalMinutes);
        if (status == TrackingSessionStatus.Paused)
        {
            return (TrackingHealth.Healthy, age); // a driver who pauses on purpose has not gone missing
        }

        return (age <= rules.StaleAfterMinutes ? TrackingHealth.Healthy : age <= rules.LostAfterMinutes ? TrackingHealth.Stale : TrackingHealth.Lost, age);
    }

    public static bool IsMoving(double? speedKph, HealthSetting rules) => speedKph is { } s && s >= rules.MovingSpeedKph;
}
