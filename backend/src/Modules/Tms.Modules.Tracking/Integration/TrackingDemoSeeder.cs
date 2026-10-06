using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Integration;

/// <summary>
/// Builds a believable day on the road through the same pipeline a real phone's locations take: trips on five lanes, ten vehicles and drivers, geofences, GPS trails, and trips in every
/// condition the control tower has to show (on time, at risk, delayed, off route, stale, lost, long stops, finished, not yet started). Nothing is inserted behind the engine's back, so what
/// the demo shows is what the engine concludes. Development and test only.
/// </summary>
internal sealed class TrackingDemoSeeder(
    TrackingDbContext db, TrackedShipmentFactory factory, SessionService sessions, LocationPipeline pipeline, TrackingHealthMonitor monitor, ICurrentUser user, TimeProvider clock) : ITrackingDemoSeeder
{
    private sealed record Lane(string Name, string FromCity, string ToCity, (double Lat, double Lon)[] Waypoints, string[] DropNames)
    {
        public double LengthKm => Enumerable.Range(1, Waypoints.Length - 1).Sum(i => Geo.DistanceKm(new GeoPoint(Waypoints[i - 1].Lat, Waypoints[i - 1].Lon), new GeoPoint(Waypoints[i].Lat, Waypoints[i].Lon)));
    }

    private static readonly Lane[] Lanes =
    [
        new("Mumbai → Pune", "Mumbai", "Pune", [(19.0760, 72.8777), (19.0000, 73.0000), (18.9000, 73.2000), (18.8000, 73.4000), (18.7500, 73.4500), (18.7000, 73.6000), (18.6000, 73.7500), (18.5204, 73.8567)], ["Pune Customer"]),
        new("Pune → Nashik", "Pune", "Nashik", [(18.5204, 73.8567), (18.9000, 73.9500), (19.1300, 73.9700), (19.5700, 74.2100), (19.8500, 74.0000), (19.9975, 73.7898)], ["Sangamner Stores", "Nashik Distribution"]),
        new("Delhi → Jaipur", "Delhi", "Jaipur", [(28.6139, 77.2090), (28.4595, 77.0266), (28.2000, 76.8500), (27.8800, 76.2800), (27.5500, 76.0000), (27.2000, 75.9000), (26.9124, 75.7873)], ["Jaipur Mart"]),
        new("Bengaluru → Chennai", "Bengaluru", "Chennai", [(12.9716, 77.5946), (13.1400, 78.1300), (12.9200, 79.1300), (13.0100, 79.8000), (13.0827, 80.2707)], ["Chennai DC"]),
        new("Hyderabad → Vijayawada", "Hyderabad", "Vijayawada", [(17.3850, 78.4867), (17.1400, 79.6200), (16.9000, 79.9000), (16.7000, 80.1000), (16.5062, 80.6480)], ["Vijayawada Retail"]),
    ];

    private static readonly string[] Vehicles = ["MH12AB1234", "MH14CD2231", "GJ05EF9087", "KA01GH4410", "TS09JK7765", "DL01LM3321", "RJ14NP8890", "TN09QR1204", "MH43ST5567", "AP16UV8081"];
    private static readonly string[] Drivers = ["Ramesh Yadav", "Sunil Patil", "Imran Sheikh", "Vikram Singh", "Anil Reddy", "Mohan Lal", "Prakash Kumar", "Suresh Naidu", "Dinesh Pawar", "Rajesh Verma"];
    private static readonly string[] Customers = ["ABC Distributors", "Surat Retail Hub", "Vadodara Stores", "Nashik Distribution", "Kolhapur Dealer", "Indore Distributor", "Ahmedabad Warehouse", "Hyderabad DC", "Bengaluru DC", "Delhi Hub"];

    private enum Story
    {
        OnTime, Deviation, ResolvedDeviation, Lost, Stale, AtRisk, SeverelyDelayed, ExcessDwell, UnplannedStop, Completed, NotStarted, Approaching, Loading,
    }

    private sealed record Trip(string Ref, int Lane, int Vehicle, Story Story, double Progress, int DelayMinutes = 0);

    private static readonly Trip[] Trips =
    [
        new("SH-10025", 0, 0, Story.Deviation, 0.55, 32),
        new("SH-10031", 1, 1, Story.Lost, 0.40),
        new("SH-10040", 2, 2, Story.AtRisk, 0.45, 24),
        new("SH-10041", 3, 3, Story.SeverelyDelayed, 0.35, 70),
        new("SH-10042", 4, 4, Story.Stale, 0.50),
        new("SH-10043", 0, 5, Story.ExcessDwell, 1.00),
        new("SH-10044", 2, 6, Story.UnplannedStop, 0.50),
        new("SH-10045", 1, 7, Story.OnTime, 0.30),
        new("SH-10046", 3, 8, Story.OnTime, 0.62),
        new("SH-10047", 4, 9, Story.OnTime, 0.20),
        new("SH-10048", 0, 0, Story.OnTime, 0.80),
        new("SH-10049", 2, 1, Story.OnTime, 0.12),
        new("SH-10050", 1, 2, Story.OnTime, 0.70),
        new("SH-10051", 3, 3, Story.OnTime, 0.15),
        new("SH-10052", 0, 4, Story.Completed, 1.00),
        new("SH-10053", 4, 5, Story.Completed, 1.00),
        new("SH-10054", 2, 6, Story.NotStarted, 0),
        new("SH-10055", 3, 7, Story.NotStarted, 0),
        new("SH-10056", 1, 8, Story.ResolvedDeviation, 0.65),
        new("SH-10057", 4, 9, Story.Approaching, 0.93),
        new("SH-10058", 0, 1, Story.Loading, 0.0),
    ];

    private const double SpeedKph = 45;

    public async Task<DemoSeedResult> SeedAsync(DemoSeedRequest request, CancellationToken cancellationToken)
    {
        if (user.TenantId is not { } tenantId)
        {
            throw new InvalidOperationException("The demo needs a signed-in tenant.");
        }

        if (request.Carriers.Count == 0)
        {
            return new DemoSeedResult(0, 0, 0, 0, "Give at least one carrier to run the demo trips.");
        }

        var now = clock.GetUtcNow();
        await EnsureGeofencesAsync(tenantId, cancellationToken);
        var made = 0;
        var points = 0;
        foreach (var trip in Trips)
        {
            if (await db.Shipments.AnyAsync(s => s.TripReference == trip.Ref, cancellationToken))
            {
                continue; // already there: the demo can be run again without doubling
            }

            var lane = Lanes[trip.Lane];
            var carrier = request.Carriers[trip.Lane % request.Carriers.Count];
            var plan = Plan(trip, lane, carrier, now);
            var created = await factory.CreateAsync(plan, cancellationToken);
            if (created.IsFailure)
            {
                continue;
            }

            await db.SaveChangesAsync(cancellationToken);
            made++;
            if (trip.Story == Story.NotStarted)
            {
                continue;
            }

            var device = $"DEMO-PHONE-{trip.Vehicle:00}";
            var started = await sessions.StartAsync(new StartTrackingRequest(trip.Ref, device, Drivers[trip.Vehicle], Vehicles[trip.Vehicle], $"demo-start-{trip.Ref}"), cancellationToken);
            if (started.IsFailure)
            {
                continue;
            }

            var trail = Trail(trip, lane, now);
            foreach (var chunk in trail.Chunk(120))
            {
                var batch = await pipeline.ProcessAsync(new LocationBatch(trip.Ref, device, chunk, "demo", 80, "Mobile data"), cancellationToken);
                points += batch.IsSuccess ? batch.Value.Accepted : 0;
            }

            if (trip.Story == Story.Completed)
            {
                await sessions.StopAsync(new StopTrackingRequest(trip.Ref, device, "Trip completed", true, $"demo-stop-{trip.Ref}"), cancellationToken);
            }
        }

        await monitor.EvaluateAsync(cancellationToken, force: true);

        // One exception has been waiting a long time, so the escalation chain can be seen at work.
        var waiting = await db.Exceptions.Where(e => e.Type == AlertType.TrackingLost && e.Status == ExceptionStatus.Open).OrderBy(e => e.RaisedAt).FirstOrDefaultAsync(cancellationToken);
        if (waiting is not null)
        {
            var id = waiting.Id;
            await db.Exceptions.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.RaisedAt, now.AddMinutes(-70)).SetProperty(e => e.DueAt, now.AddMinutes(-10)), cancellationToken);
            await monitor.EvaluateAsync(cancellationToken, force: true);
        }

        var alerts = await db.Alerts.CountAsync(cancellationToken);
        var exceptions = await db.Exceptions.CountAsync(cancellationToken);
        return new DemoSeedResult(made, points, alerts, exceptions, made == 0 ? "The demo trips are already there." : $"Seeded {made} trips with {points} locations.");
    }

    // ---- the plan a trip was given

    private static PlannedTrackingContext Plan(Trip trip, Lane lane, DemoCarrier carrier, DateTimeOffset now)
    {
        var total = lane.LengthKm;
        var routePoints = Densify(lane.Waypoints).Select(p => new TrackingPoint(p.Lat, p.Lon)).ToList();
        var stops = new List<TrackingStopFact>
        {
            new(null, 1, "Pickup", $"{lane.FromCity} DC", lane.FromCity, lane.Waypoints[0].Lat, lane.Waypoints[0].Lon, null, null, null, null, $"ORD-{trip.Ref}", null, $"{lane.FromCity} DC"),
        };

        // Planned times are set from where the vehicle will be, so the story (late by this much) is true by construction.
        var speed = SpeedKph;
        double Along(double fraction) => fraction * total;
        var elapsedToNow = Along(Math.Max(trip.Progress, 0)) / speed * 60;
        var startAt = now.AddMinutes(-elapsedToNow - 30);
        var drops = lane.DropNames.Length;
        for (var i = 0; i < drops; i++)
        {
            var fraction = drops == 1 ? 1.0 : i == drops - 1 ? 1.0 : 0.55;
            var (lat, lon) = PointAt(lane, fraction);
            var remainingMinutes = Math.Max(0, Along(fraction) - Along(trip.Progress)) / speed * 60;
            // What the engine will add: the expected standing time at each earlier drop (30 min) and, while loading, what is left of the pickup's hour.
            var onTimeEta = trip.Story is Story.NotStarted ? now.AddHours(1).AddMinutes(Along(fraction) / speed * 60) : now.AddMinutes(remainingMinutes + 30 * i + (trip.Story == Story.Loading ? 32 : 0));
            var planned = onTimeEta.AddMinutes(-trip.DelayMinutes);
            if (trip.Story is Story.ExcessDwell or Story.Completed)
            {
                planned = now.AddHours(i == drops - 1 ? 2 : 1);
            }

            stops.Add(new TrackingStopFact(
                Guid.NewGuid(), i + 2, "Drop", lane.DropNames[i], lane.DropNames[i].Split(' ')[0], lat, lon, planned, null, null, null, $"INV-{trip.Ref}-{i + 1}", $"CUST-{trip.Lane}{i}", Customers[(trip.Lane * 2 + i) % Customers.Length]));
        }

        stops[0] = stops[0] with { PlannedArrival = startAt };
        return new PlannedTrackingContext(
            DeterministicId(trip.Ref), trip.Ref, trip.Ref, carrier.TransporterId, carrier.Name, Vehicles[trip.Vehicle], Drivers[trip.Vehicle], $"98{76000000 + trip.Vehicle * 1_234_567:00000000}",
            startAt, (decimal)Math.Round(total * 1.15, 1), (int)(total * 1.15 / speed * 60), "Osrm", routePoints, stops);
    }

    // ---- the GPS trail a story leaves

    private static List<LocationPoint> Trail(Trip trip, Lane lane, DateTimeOffset now)
    {
        var total = lane.LengthKm;
        // Segments in the order they happened: (from, to, minutes, speed) or a stay (at, minutes), with an optional sideways offset in km.
        var segments = new List<(double From, double To, int Minutes, double Speed, double OffKm)>();
        void Drive(double from, double to, double offKm = 0)
        {
            var minutes = (int)Math.Max(6, Math.Abs(to - from) * total / SpeedKph * 60);
            segments.Add((from, to, minutes, SpeedKph, offKm));
        }

        void Stay(double at, int minutes, double offKm = 0) => segments.Add((at, at, minutes, 0, offKm));

        var progress = trip.Progress;
        int lastFixAgo = 1;
        switch (trip.Story)
        {
            case Story.Loading:
                Stay(0, 28);
                break;
            case Story.Completed:
                Stay(0, 20);
                Drive(0, 1.0);
                Stay(1.0, 14);
                lastFixAgo = 25 + (trip.Ref.GetHashCode(StringComparison.Ordinal) & 15);
                break;
            case Story.ExcessDwell:
                Stay(0, 20);
                Drive(0, 0.97);
                Drive(0.97, 1.0);
                Stay(1.0, 80);
                break;
            case Story.UnplannedStop:
                Stay(0, 20);
                Drive(0, progress);
                Stay(progress, 42);
                break;
            case Story.Deviation:
                Stay(0, 22);
                Drive(0, progress - 0.03);
                Drive(progress - 0.03, progress, offKm: 3.8);
                Stay(progress, 14, offKm: 3.8);
                break;
            case Story.ResolvedDeviation:
                Stay(0, 20);
                Drive(0, progress - 0.1);
                Drive(progress - 0.1, progress - 0.06, offKm: 4.5);
                Stay(progress - 0.06, 14, offKm: 4.5);
                Drive(progress - 0.06, progress);
                break;
            case Story.Lost:
                Stay(0, 20);
                Drive(0, progress);
                lastFixAgo = 36;
                break;
            case Story.Stale:
                Stay(0, 20);
                Drive(0, progress);
                lastFixAgo = 15;
                break;
            default:
                Stay(0, 20);
                Drive(0, progress);
                break;
        }

        var totalMinutes = segments.Sum(s => s.Minutes);
        var end = now.AddMinutes(-lastFixAgo);
        var start = end.AddMinutes(-totalMinutes);
        var points = new List<LocationPoint>();
        var clock = start;
        var rng = new Random(trip.Ref.GetHashCode(StringComparison.Ordinal));
        var id = 0;
        foreach (var (from, to, minutes, speed, offKm) in segments)
        {
            var steps = Math.Max(1, minutes / 3);
            for (var i = 0; i <= steps; i++)
            {
                var t = steps == 0 ? 1 : (double)i / steps;
                var fraction = from + (to - from) * t;
                var (lat, lon) = PointAt(lane, fraction);
                if (offKm > 0)
                {
                    (lat, lon) = Sideways(lane, fraction, offKm);
                }

                lat += (rng.NextDouble() - 0.5) * 0.0002;
                lon += (rng.NextDouble() - 0.5) * 0.0002;
                var at = clock.AddMinutes(minutes * t);
                points.Add(new LocationPoint($"DEMO-{trip.Ref}-{++id:0000}", lat, lon, 8 + rng.Next(0, 8), speed == 0 ? 0 : speed + rng.Next(-3, 4), 180, at));
            }

            clock = clock.AddMinutes(minutes);
        }

        return [.. points.OrderBy(p => p.CapturedAtUtc).Where(p => p.CapturedAtUtc <= now.AddMinutes(-1))];
    }

    // ---- geometry helpers

    private static (double Lat, double Lon) PointAt(Lane lane, double fraction)
    {
        var w = lane.Waypoints;
        var lengths = new double[w.Length];
        for (var i = 1; i < w.Length; i++)
        {
            lengths[i] = lengths[i - 1] + Geo.DistanceKm(new GeoPoint(w[i - 1].Lat, w[i - 1].Lon), new GeoPoint(w[i].Lat, w[i].Lon));
        }

        var target = Math.Clamp(fraction, 0, 1) * lengths[^1];
        for (var i = 1; i < w.Length; i++)
        {
            if (target <= lengths[i])
            {
                var t = (target - lengths[i - 1]) / Math.Max(lengths[i] - lengths[i - 1], 1e-9);
                return (w[i - 1].Lat + (w[i].Lat - w[i - 1].Lat) * t, w[i - 1].Lon + (w[i].Lon - w[i - 1].Lon) * t);
            }
        }

        return w[^1];
    }

    private static (double Lat, double Lon) Sideways(Lane lane, double fraction, double km)
    {
        var (lat, lon) = PointAt(lane, fraction);
        var (aLat, aLon) = PointAt(lane, Math.Min(1, fraction + 0.01));
        var (bLat, bLon) = PointAt(lane, Math.Max(0, fraction - 0.01));
        var cos = Math.Cos(lat * Math.PI / 180);
        var east = (aLon - bLon) * cos;
        var north = aLat - bLat;
        var length = Math.Max(Math.Sqrt(east * east + north * north), 1e-9);
        return (lat + -east / length * km / 111.195, lon + north / length * km / (111.195 * cos));
    }

    /// <summary>A polyline with a point about every kilometre, so the road looks like a road and a vehicle's distance from it is measured against something fine.</summary>
    private static List<(double Lat, double Lon)> Densify((double Lat, double Lon)[] waypoints)
    {
        var result = new List<(double Lat, double Lon)> { waypoints[0] };
        for (var i = 1; i < waypoints.Length; i++)
        {
            var km = Geo.DistanceKm(new GeoPoint(waypoints[i - 1].Lat, waypoints[i - 1].Lon), new GeoPoint(waypoints[i].Lat, waypoints[i].Lon));
            var steps = Math.Max(1, (int)(km / 2));
            for (var s = 1; s <= steps; s++)
            {
                var t = (double)s / steps;
                result.Add((waypoints[i - 1].Lat + (waypoints[i].Lat - waypoints[i - 1].Lat) * t, waypoints[i - 1].Lon + (waypoints[i].Lon - waypoints[i - 1].Lon) * t));
            }
        }

        return result;
    }

    private static Guid DeterministicId(string reference)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"tracking-demo:{reference}"))[..16];
        return new Guid(bytes);
    }

    private async Task EnsureGeofencesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = (await db.Geofences.Select(g => g.Code).ToListAsync(cancellationToken)).ToHashSet();
        (string Code, string Name, GeofenceType Type, double Lat, double Lon, int Radius)[] fences =
        [
            ("MUM-DC", "Mumbai DC", GeofenceType.Warehouse, 19.0760, 72.8777, 400), ("PUN-HUB", "Pune Hub", GeofenceType.Hub, 18.5204, 73.8567, 500),
            ("DEL-DC", "Delhi DC", GeofenceType.Warehouse, 28.6139, 77.2090, 400), ("BLR-DC", "Bengaluru DC", GeofenceType.Warehouse, 12.9716, 77.5946, 400),
            ("KHL-TOLL", "Khalapur toll plaza", GeofenceType.Toll, 18.8000, 73.4000, 300), ("GHAT-RZ", "Ghat restricted zone", GeofenceType.RestrictedArea, 18.7500, 73.4500, 600),
            ("NH48-HR", "High-risk stretch NH48", GeofenceType.HighRiskZone, 27.5500, 76.0000, 800),
        ];
        foreach (var f in fences.Where(f => !existing.Contains(f.Code)))
        {
            var created = Geofence.Create(tenantId, f.Code, f.Name, f.Type, f.Lat, f.Lon, f.Radius, null, null, null);
            if (created.IsSuccess)
            {
                db.Geofences.Add(created.Value);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
