using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.Modules.Shipments.Domain;

namespace Tms.Modules.Shipments.Infrastructure.Routing;

/// <summary>Settings under <c>Routing</c>. Leave <see cref="OsrmBaseUrl"/> empty to use estimates only.</summary>
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    /// <summary>Base URL of your own OSRM server, e.g. <c>http://osrm:5000</c>. The public demo server is not for production use.</summary>
    public string? OsrmBaseUrl { get; set; }

    /// <summary>Road distance is longer than the straight line; typical for Indian highways.</summary>
    public double CircuityFactor { get; set; } = 1.3;

    public double AverageSpeedKmh { get; set; } = 45;

    public int OsrmTimeoutSeconds { get; set; } = 5;
}

/// <summary>Straight-line distance × a road factor, at an average truck speed. Always available; always labelled an estimate.</summary>
public sealed class EstimatedRoutingProvider(IOptions<RoutingOptions> options) : IRoutingProvider
{
    public Task<RouteResult> GetRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var legs = new List<RouteLeg>();
        for (var i = 1; i < waypoints.Count; i++)
        {
            var km = waypoints[i - 1].StraightLineKm(waypoints[i]) * o.CircuityFactor;
            legs.Add(new RouteLeg(Math.Round(km, 2), Math.Round(km / Math.Max(o.AverageSpeedKmh, 1) * 60, 1)));
        }

        return Task.FromResult(new RouteResult(legs, RouteSource.Estimate));
    }

    public Task<DistanceMatrix> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var km = new double[points.Count, points.Count];
        var minutes = new double[points.Count, points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = 0; j < points.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                km[i, j] = Math.Round(points[i].StraightLineKm(points[j]) * o.CircuityFactor, 2);
                minutes[i, j] = Math.Round(km[i, j] / Math.Max(o.AverageSpeedKmh, 1) * 60, 1);
            }
        }

        return Task.FromResult(new DistanceMatrix(km, minutes, RouteSource.Estimate));
    }
}

/// <summary>Road routing from an OSRM server (open source, self-hosted). Throws on any failure; wrap it in <see cref="ResilientRoutingProvider"/>.</summary>
public sealed class OsrmRoutingProvider(HttpClient http, IOptions<RoutingOptions> options) : IRoutingProvider
{
    public async Task<RouteResult> GetRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        var coordinates = string.Join(';', waypoints.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Longitude:0.######},{p.Latitude:0.######}")));
        var url = $"{options.Value.OsrmBaseUrl!.TrimEnd('/')}/route/v1/driving/{coordinates}?overview=false&steps=false";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.OsrmTimeoutSeconds)));
        using var response = await http.GetAsync(new Uri(url), timeout.Token);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(timeout.Token), waypoints.Count - 1);
    }

    public async Task<IReadOnlyList<GeoPoint>?> GetGeometryAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        var coordinates = string.Join(';', waypoints.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Longitude:0.######},{p.Latitude:0.######}")));
        var url = $"{options.Value.OsrmBaseUrl!.TrimEnd('/')}/route/v1/driving/{coordinates}?overview=full&geometries=geojson&steps=false";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.OsrmTimeoutSeconds)));
        using var response = await http.GetAsync(new Uri(url), timeout.Token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetString() != "Ok" || !root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OSRM found no route.");
        }

        return routes[0].GetProperty("geometry").GetProperty("coordinates").EnumerateArray().Select(c => new GeoPoint(c[1].GetDouble(), c[0].GetDouble())).ToList();
    }

    public async Task<DistanceMatrix> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken)
    {
        var coordinates = string.Join(';', points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Longitude:0.######},{p.Latitude:0.######}")));
        var url = $"{options.Value.OsrmBaseUrl!.TrimEnd('/')}/table/v1/driving/{coordinates}?annotations=duration,distance";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.OsrmTimeoutSeconds)));
        using var response = await http.GetAsync(new Uri(url), timeout.Token);
        response.EnsureSuccessStatusCode();
        return ParseMatrix(await response.Content.ReadAsStringAsync(timeout.Token), points.Count);
    }

    /// <summary>Reads OSRM's table service (<c>distances</c> in metres, <c>durations</c> in seconds).</summary>
    public static DistanceMatrix ParseMatrix(string json, int size)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetString() != "Ok"
            || !root.TryGetProperty("distances", out var distances) || !root.TryGetProperty("durations", out var durations))
        {
            throw new InvalidOperationException("OSRM returned no matrix.");
        }

        var km = new double[size, size];
        var minutes = new double[size, size];
        if (distances.GetArrayLength() != size || durations.GetArrayLength() != size)
        {
            throw new InvalidOperationException("OSRM returned a matrix of the wrong size.");
        }

        for (var i = 0; i < size; i++)
        {
            var d = distances[i];
            var t = durations[i];
            if (d.GetArrayLength() != size || t.GetArrayLength() != size)
            {
                throw new InvalidOperationException("OSRM returned a matrix of the wrong size.");
            }

            for (var j = 0; j < size; j++)
            {
                // null means the pair is unreachable by road; treat that as a failure so the caller falls back rather than plan a fiction.
                if (d[j].ValueKind == JsonValueKind.Null || t[j].ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidOperationException("OSRM has no road between two of the points.");
                }

                km[i, j] = Math.Round(d[j].GetDouble() / 1000, 2);
                minutes[i, j] = Math.Round(t[j].GetDouble() / 60, 1);
            }
        }

        return new DistanceMatrix(km, minutes, RouteSource.Osrm);
    }

    /// <summary>Reads OSRM's <c>routes[0].legs[]</c> (distance in metres, duration in seconds).</summary>
    public static RouteResult Parse(string json, int expectedLegs)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetString() != "Ok" || !root.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OSRM found no route.");
        }

        var legs = routes[0].GetProperty("legs").EnumerateArray()
            .Select(l => new RouteLeg(Math.Round(l.GetProperty("distance").GetDouble() / 1000, 2), Math.Round(l.GetProperty("duration").GetDouble() / 60, 1)))
            .ToList();
        if (legs.Count != expectedLegs)
        {
            throw new InvalidOperationException($"OSRM returned {legs.Count} legs, expected {expectedLegs}.");
        }

        return new RouteResult(legs, RouteSource.Osrm);
    }
}

/// <summary>Uses OSRM when configured and reachable, otherwise the estimate — and reports which, so nothing is passed off as road distance.</summary>
public sealed class ResilientRoutingProvider(
    IOptions<RoutingOptions> options, IServiceProvider services, EstimatedRoutingProvider estimate, IMemoryCache cache, ILogger<ResilientRoutingProvider> logger) : IRoutingProvider
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    public async Task<RouteResult> GetRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        if (waypoints.Count < 2)
        {
            return new RouteResult([], RouteSource.Estimate);
        }

        if (string.IsNullOrWhiteSpace(options.Value.OsrmBaseUrl))
        {
            return await estimate.GetRouteAsync(waypoints, cancellationToken);
        }

        var key = "route:" + string.Join('|', waypoints.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Latitude:0.####},{p.Longitude:0.####}")));
        if (cache.TryGetValue(key, out RouteResult? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var osrm = (OsrmRoutingProvider)services.GetService(typeof(OsrmRoutingProvider))!;
            var result = await osrm.GetRouteAsync(waypoints, cancellationToken);
            cache.Set(key, result, CacheFor);
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(e, "OSRM routing failed; using an estimate instead.");
            return await estimate.GetRouteAsync(waypoints, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<GeoPoint>?> GetGeometryAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken cancellationToken)
    {
        if (waypoints.Count < 2 || string.IsNullOrWhiteSpace(options.Value.OsrmBaseUrl))
        {
            return null;
        }

        try
        {
            var osrm = (OsrmRoutingProvider)services.GetService(typeof(OsrmRoutingProvider))!;
            return await osrm.GetGeometryAsync(waypoints, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(e, "OSRM route geometry failed; the straight line between stops will be used.");
            return null;
        }
    }

    public async Task<DistanceMatrix> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken)
    {
        if (points.Count < 2 || string.IsNullOrWhiteSpace(options.Value.OsrmBaseUrl))
        {
            return await estimate.GetMatrixAsync(points, cancellationToken);
        }

        var key = "matrix:" + string.Join('|', points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Latitude:0.####},{p.Longitude:0.####}")));
        if (cache.TryGetValue(key, out DistanceMatrix? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var osrm = (OsrmRoutingProvider)services.GetService(typeof(OsrmRoutingProvider))!;
            var result = await osrm.GetMatrixAsync(points, cancellationToken);
            cache.Set(key, result, CacheFor);
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(e, "OSRM matrix failed; using an estimate instead.");
            return await estimate.GetMatrixAsync(points, cancellationToken);
        }
    }
}
