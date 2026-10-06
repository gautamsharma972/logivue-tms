namespace Tms.Modules.Tracking.Domain;

public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>Distances on the earth's surface. Pure arithmetic: no provider, no database.</summary>
public static class Geo
{
    private const double EarthRadiusKm = 6371.0088;

    public static double ToRadians(double degrees) => degrees * Math.PI / 180;

    public static bool IsValid(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 && !double.IsNaN(latitude) && !double.IsNaN(longitude);

    /// <summary>Great-circle distance in kilometres.</summary>
    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static double DistanceMetres(GeoPoint a, GeoPoint b) => DistanceKm(a, b) * 1000;

    /// <summary>Compass bearing from a to b, 0–360.</summary>
    public static double BearingDegrees(GeoPoint a, GeoPoint b)
    {
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var y = Math.Sin(dLon) * Math.Cos(ToRadians(b.Latitude));
        var x = Math.Cos(ToRadians(a.Latitude)) * Math.Sin(ToRadians(b.Latitude)) - Math.Sin(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude)) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>Ray casting: is the point inside the polygon (a closed ring of at least three vertices)?</summary>
    public static bool InPolygon(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count < 3)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude)
                && point.Longitude < (b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Distance in kilometres from the point to the nearest edge of the polygon.</summary>
    public static double DistanceToPolygonEdgeKm(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        var best = double.MaxValue;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            best = Math.Min(best, new RouteGeometry([ring[j], ring[i]]).Match(point).OffRouteKm);
        }

        return best;
    }
}

/// <param name="AlongKm">How far along the route the nearest point is, from its start.</param>
/// <param name="OffRouteKm">How far the vehicle is from that nearest point.</param>
public readonly record struct RouteMatch(double AlongKm, double OffRouteKm, int SegmentIndex);

/// <summary>A route as a polyline, measured once so a vehicle's position can be placed on it: how far along, and how far off.</summary>
public sealed class RouteGeometry
{
    private readonly GeoPoint[] _points;
    private readonly double[] _cumulativeKm;

    public RouteGeometry(IReadOnlyList<GeoPoint> points)
    {
        _points = [.. points];
        _cumulativeKm = new double[_points.Length];
        for (var i = 1; i < _points.Length; i++)
        {
            _cumulativeKm[i] = _cumulativeKm[i - 1] + Geo.DistanceKm(_points[i - 1], _points[i]);
        }
    }

    public IReadOnlyList<GeoPoint> Points => _points;

    public double LengthKm => _points.Length == 0 ? 0 : _cumulativeKm[^1];

    public bool IsUsable => _points.Length >= 2 && LengthKm > 0;

    /// <summary>Distance from the start of the route to the given vertex.</summary>
    public double AlongKmAt(int vertex) => _cumulativeKm[Math.Clamp(vertex, 0, _cumulativeKm.Length - 1)];

    /// <summary>
    /// The nearest place on the route. A route can double back on itself, so when <paramref name="notBeforeKm"/> is given, places earlier than that (minus a little for GPS
    /// noise) are ignored unless nothing later is close: progress does not jump backwards just because the road passes the same spot twice.
    /// </summary>
    public RouteMatch Match(GeoPoint point, double? notBeforeKm = null)
    {
        if (_points.Length == 0)
        {
            return new RouteMatch(0, double.MaxValue, 0);
        }

        if (_points.Length == 1)
        {
            return new RouteMatch(0, Geo.DistanceKm(_points[0], point), 0);
        }

        RouteMatch? best = null;
        RouteMatch? bestForward = null;
        var floor = notBeforeKm is { } n ? n - 0.5 : double.MinValue;
        for (var i = 0; i < _points.Length - 1; i++)
        {
            var (t, off) = Project(_points[i], _points[i + 1], point);
            var along = _cumulativeKm[i] + t * (_cumulativeKm[i + 1] - _cumulativeKm[i]);
            var candidate = new RouteMatch(along, off, i);
            if (best is null || off < best.Value.OffRouteKm)
            {
                best = candidate;
            }

            if (along >= floor && (bestForward is null || off < bestForward.Value.OffRouteKm))
            {
                bestForward = candidate;
            }
        }

        // Prefer the forward place unless it is much farther than the true nearest (the vehicle really did go somewhere else).
        return bestForward is { } forward && forward.OffRouteKm <= best!.Value.OffRouteKm + 1.0 ? forward : best!.Value;
    }

    /// <summary>Projects the point onto the segment in a local flat approximation: accurate to metres over the few kilometres that matter here.</summary>
    private static (double T, double OffKm) Project(GeoPoint a, GeoPoint b, GeoPoint p)
    {
        var cosLat = Math.Cos(Geo.ToRadians((a.Latitude + b.Latitude) / 2));
        const double KmPerDegree = 111.195;
        double Dx(GeoPoint q) => (q.Longitude - a.Longitude) * cosLat * KmPerDegree;
        double Dy(GeoPoint q) => (q.Latitude - a.Latitude) * KmPerDegree;

        var bx = Dx(b);
        var by = Dy(b);
        var px = Dx(p);
        var py = Dy(p);
        var lengthSquared = bx * bx + by * by;
        var t = lengthSquared < 1e-12 ? 0 : Math.Clamp((px * bx + py * by) / lengthSquared, 0, 1);
        var dx = px - t * bx;
        var dy = py - t * by;
        return (t, Math.Sqrt(dx * dx + dy * dy));
    }
}
