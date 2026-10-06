namespace Tms.Modules.Tracking.Domain;

/// <summary>A point of a trip's recorded path, kept after the raw GPS is gone: position and the moment it was captured.</summary>
public readonly record struct PathPoint(double Latitude, double Longitude, long EpochSeconds);

/// <summary>
/// Reduces a recorded path to the points that carry its shape (Douglas-Peucker), so a finished trip can still be replayed after its raw GPS has been purged. The first and last
/// points are always kept, and so is any point after a long silence, so a gap in tracking stays visible as a straight jump rather than being smoothed over.
/// </summary>
public static class RouteSimplifier
{
    public static IReadOnlyList<PathPoint> Simplify(IReadOnlyList<PathPoint> path, double toleranceMetres, int maxPoints = 1200, int keepAfterGapSeconds = 900)
    {
        if (path.Count <= 2)
        {
            return path.ToList();
        }

        var keep = new bool[path.Count];
        keep[0] = keep[^1] = true;
        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].EpochSeconds - path[i - 1].EpochSeconds >= keepAfterGapSeconds)
            {
                keep[i] = keep[i - 1] = true;
            }
        }

        // Iterative so a long trip cannot overflow the stack: segments between kept points are refined until nothing is further than the tolerance.
        var stack = new Stack<(int From, int To)>();
        var anchors = Enumerable.Range(0, path.Count).Where(i => keep[i]).ToList();
        for (var i = 1; i < anchors.Count; i++)
        {
            stack.Push((anchors[i - 1], anchors[i]));
        }

        while (stack.Count > 0)
        {
            var (from, to) = stack.Pop();
            if (to - from < 2)
            {
                continue;
            }

            var worst = -1;
            var worstDistance = toleranceMetres;
            for (var i = from + 1; i < to; i++)
            {
                var d = DistanceToSegmentMetres(path[i], path[from], path[to]);
                if (d > worstDistance)
                {
                    worstDistance = d;
                    worst = i;
                }
            }

            if (worst >= 0)
            {
                keep[worst] = true;
                stack.Push((from, worst));
                stack.Push((worst, to));
            }
        }

        var result = path.Where((_, i) => keep[i]).ToList();
        if (result.Count > maxPoints)
        {
            // Still too many (a very winding trip): even thinning, ends kept.
            result = Enumerable.Range(0, maxPoints).Select(i => result[(int)Math.Round(i * (result.Count - 1.0) / (maxPoints - 1))]).ToList();
        }

        return result;
    }

    /// <summary>Distance from a point to a segment, on a flat local projection (accurate to well under a metre over the few kilometres a segment spans).</summary>
    internal static double DistanceToSegmentMetres(PathPoint p, PathPoint a, PathPoint b)
    {
        var cos = Math.Cos(Geo.ToRadians(a.Latitude));
        double X(PathPoint q) => (q.Longitude - a.Longitude) * cos * 111_195;
        double Y(PathPoint q) => (q.Latitude - a.Latitude) * 111_195;
        var (px, py, bx, by) = (X(p), Y(p), X(b), Y(b));
        var lengthSquared = bx * bx + by * by;
        var t = lengthSquared < 1e-9 ? 0 : Math.Clamp((px * bx + py * by) / lengthSquared, 0, 1);
        var (dx, dy) = (px - t * bx, py - t * by);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
