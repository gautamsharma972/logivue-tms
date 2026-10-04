namespace Tms.Modules.Shipments.Domain;

public sealed record GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>India's bounding box, with margin. Catches swapped or mistyped coordinates (a common data-entry error).</summary>
    public const double MinLatitude = 6, MaxLatitude = 38, MinLongitude = 68, MaxLongitude = 98;

    public bool IsInIndia => Latitude is >= MinLatitude and <= MaxLatitude && Longitude is >= MinLongitude and <= MaxLongitude;

    /// <summary>Great-circle distance. Only an input to the estimate; real planning prefers road distance.</summary>
    public double StraightLineKm(GeoPoint other)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(other.Latitude - Latitude);
        var dLon = Rad(other.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(Latitude)) * Math.Cos(Rad(other.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

/// <summary>Where a route figure came from. An estimate must never be presented as a measured road distance.</summary>
public enum RouteSource
{
    Estimate = 1,
    Osrm = 2,
}

public sealed record RouteLeg(double DistanceKm, double DurationMinutes);

/// <param name="Legs">One leg per consecutive pair of waypoints.</param>
public sealed record RouteResult(IReadOnlyList<RouteLeg> Legs, RouteSource Source)
{
    public double TotalKm => Legs.Sum(l => l.DistanceKm);

    public double TotalMinutes => Legs.Sum(l => l.DurationMinutes);
}

/// <summary>Pairwise road distance (km) and driving time (minutes) between points: <c>Km[i, j]</c> is from point i to point j.</summary>
public sealed record DistanceMatrix(double[,] Km, double[,] Minutes, RouteSource Source)
{
    public int Size => Km.GetLength(0);
}

/// <summary>
/// Road distance and driving time between points. Kept apart from the planning rules so the source (open-source OSRM, an
/// estimate, another provider later) can change without touching them. Implementations must not throw for provider outages:
/// they fall back to an estimate and say so in <see cref="RouteResult.Source"/>.
/// </summary>
public interface IRoutingProvider
{
    Task<RouteResult> GetRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken);

    /// <summary>All pairwise distances, so stop order can be searched without one routing call per candidate order.</summary>
    Task<DistanceMatrix> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken);
}
