namespace Tms.SharedKernel.Geo;

/// <summary>Great-circle distance for modules that need a geofence decision (Haversine, mean Earth radius).</summary>
public static class GeoMath
{
    private const double EarthRadiusMetres = 6_371_000;

    public static double DistanceMetres(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(latitudeB - latitudeA);
        var dLon = Rad(longitudeB - longitudeA);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(latitudeA)) * Math.Cos(Rad(latitudeB)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMetres * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
