namespace Tms.Modules.Deliveries.Domain;

public static class Geo
{
    private const double EarthRadiusM = 6_371_000;

    public static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static bool IsValid(double? latitude, double? longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 && !(latitude == 0 && longitude == 0);

    /// <summary>Where the driver was against the customer's geofence. A missing or too-vague fix is reported as such, not as "outside".</summary>
    public static GeofenceStatus Check(
        double? customerLat, double? customerLon, int? radiusM, double? driverLat, double? driverLon, double? accuracyM, double maxAccuracyM)
    {
        if (!IsValid(customerLat, customerLon) || radiusM is not > 0)
        {
            return GeofenceStatus.NotApplicable;
        }

        if (!IsValid(driverLat, driverLon))
        {
            return GeofenceStatus.GpsUnavailable;
        }

        if (accuracyM is { } accuracy && accuracy > maxAccuracyM)
        {
            return GeofenceStatus.AccuracyInsufficient;
        }

        return DistanceMetres(customerLat!.Value, customerLon!.Value, driverLat!.Value, driverLon!.Value) <= radiusM ? GeofenceStatus.Inside : GeofenceStatus.Outside;
    }
}
