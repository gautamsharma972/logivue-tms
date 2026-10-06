using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Transporters.Application;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Messaging;

namespace Tms.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for planning: tests say what a trip looks like and Tracking reads it through the same contract it uses for Shipments. A trip the test did not describe is looked up in
/// Shipments for real, so a dispatched shipment still becomes a tracked trip.
/// </summary>
internal sealed class TestTrackingPlanning(IServiceProvider services) : ITrackingPlanningIntegration
{
    private static readonly ConcurrentDictionary<string, PlannedTrackingContext> ByTrip = new();

    public static void Add(PlannedTrackingContext plan) => ByTrip[plan.TripReference] = plan;

    public Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(string tripReference, CancellationToken cancellationToken) =>
        ByTrip.TryGetValue(tripReference, out var plan) ? Task.FromResult<PlannedTrackingContext?>(plan) : Real().GetPlannedTrackingContextAsync(tripReference, cancellationToken);

    public Task<PlannedTrackingContext?> GetPlannedTrackingContextAsync(Guid shipmentId, CancellationToken cancellationToken) =>
        ByTrip.Values.FirstOrDefault(p => p.ShipmentId == shipmentId) is { } plan ? Task.FromResult<PlannedTrackingContext?>(plan) : Real().GetPlannedTrackingContextAsync(shipmentId, cancellationToken);

    private Tms.Modules.Shipments.Integration.ShipmentTrackingFeed Real() => ActivatorUtilities.CreateInstance<Tms.Modules.Shipments.Integration.ShipmentTrackingFeed>(services);
}

public sealed class TrackingEventLog
{
    public ConcurrentQueue<IDomainEvent> Events { get; } = new();

    public IEnumerable<T> Of<T>(string tripReference) where T : TrackingEvent => Events.OfType<T>().Where(e => e.TripReference == tripReference);
}

internal sealed class TrackingEventRecorder<T>(TrackingEventLog log) : IDomainEventHandler<T> where T : IDomainEvent
{
    public Task HandleAsync(T domainEvent, CancellationToken cancellationToken)
    {
        log.Events.Enqueue(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>A trip from Mumbai to Pune with a driver's login, a rival carrier's login, and helpers to send the locations a phone would.</summary>
internal sealed class TrackingScenario : IDisposable
{
    public static readonly (double Lat, double Lon)[] Road =
    [
        (19.0760, 72.8777), (19.0000, 73.0000), (18.9000, 73.2000), (18.8000, 73.4000), (18.7500, 73.4500), (18.7000, 73.6000), (18.6000, 73.7500), (18.5204, 73.8567),
    ];

    public required TmsApiFactory Factory { get; init; }

    public required HttpClient Admin { get; init; }

    public required HttpClient Driver { get; init; }

    public required HttpClient Rival { get; init; }

    public required TransporterDto Transporter { get; init; }

    public required string TripReference { get; init; }

    public required Guid ShipmentId { get; init; }

    public string DeviceId { get; } = $"DEVICE-{Guid.NewGuid():N}"[..14];

    public string VehicleReference { get; } = $"MH12{Random.Shared.Next(1000, 9999)}";

    public static async Task<TrackingScenario> CreateAsync(TmsApiFactory factory, TimeSpan? plannedArrivalIn = null, bool withRoute = true, Action<List<TrackingStopFact>>? stops = null)
    {
        var admin = await factory.AdminAsync();
        var transporter = await ContractApiData.ActiveTransporterAsync(admin);
        var rival = await ContractApiData.ActiveTransporterAsync(admin);
        var trip = $"SH-T{Guid.NewGuid():N}"[..14];
        var scenario = new TrackingScenario
        {
            Factory = factory, Admin = admin, Transporter = transporter, TripReference = trip, ShipmentId = Guid.NewGuid(),
            Driver = await VendorForAsync(factory, admin, transporter.Id), Rival = await VendorForAsync(factory, admin, rival.Id),
        };
        TestTrackingPlanning.Add(scenario.Plan(plannedArrivalIn ?? TimeSpan.FromHours(4), withRoute, stops));
        return scenario;
    }

    private static async Task<HttpClient> VendorForAsync(TmsApiFactory factory, HttpClient admin, Guid transporterId)
    {
        var role = await admin.CreateExternalRoleAsync(TrackingPermissions.Execute);
        var email = ApiExtensions.UniqueEmail("driver");
        var created = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(email, "Driver", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporterId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);
    }

    public PlannedTrackingContext Plan(TimeSpan plannedArrivalIn, bool withRoute = true, Action<List<TrackingStopFact>>? tweak = null)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<TrackingStopFact>
        {
            new(null, 1, "Pickup", "Mumbai DC", "Mumbai", Road[0].Lat, Road[0].Lon, now.AddMinutes(-30), null, null, null, "ORD-PICK", null, "Mumbai DC"),
            new(Guid.NewGuid(), 2, "Drop", "Pune Customer", "Pune", Road[^1].Lat, Road[^1].Lon, now.Add(plannedArrivalIn), null, null, null, "INV-1", "CUST-1", "ABC Distributors"),
        };
        tweak?.Invoke(list);
        var route = withRoute ? Road.Select(p => new TrackingPoint(p.Lat, p.Lon)).ToList() : [];
        return new PlannedTrackingContext(
            ShipmentId, TripReference, TripReference, Transporter.Id, Transporter.LegalName, VehicleReference, "Ramesh Yadav", "9876543210", now.AddMinutes(-30), 150m, 240, withRoute ? "Osrm" : "Estimate", route, list);
    }

    /// <summary>A position a given fraction of the way along the road (0 = Mumbai, 1 = Pune).</summary>
    public static (double Lat, double Lon) Along(double fraction)
    {
        var lengths = new double[Road.Length];
        for (var i = 1; i < Road.Length; i++)
        {
            lengths[i] = lengths[i - 1] + Distance(Road[i - 1], Road[i]);
        }

        var target = Math.Clamp(fraction, 0, 1) * lengths[^1];
        for (var i = 1; i < Road.Length; i++)
        {
            if (target <= lengths[i])
            {
                var t = (target - lengths[i - 1]) / (lengths[i] - lengths[i - 1]);
                return (Road[i - 1].Lat + (Road[i].Lat - Road[i - 1].Lat) * t, Road[i - 1].Lon + (Road[i].Lon - Road[i - 1].Lon) * t);
            }
        }

        return Road[^1];
    }

    private static double Distance((double Lat, double Lon) a, (double Lat, double Lon) b) => Geo.DistanceKm(new GeoPoint(a.Lat, a.Lon), new GeoPoint(b.Lat, b.Lon));

    private int _counter;

    public LocationPoint Fix(double fraction, int minutesAgo, double? speed = 45, double? accuracy = 10)
    {
        var (lat, lon) = Along(fraction);
        return At(lat, lon, minutesAgo, speed, accuracy);
    }

    public LocationPoint At(double lat, double lon, int minutesAgo, double? speed = 45, double? accuracy = 10) =>
        new($"LOC-{Interlocked.Increment(ref _counter):0000}", lat, lon, accuracy, speed, 180, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    /// <summary>Straight off the route by roughly the given kilometres (east).</summary>
    public LocationPoint OffRoute(double fraction, double km, int minutesAgo, double? speed = 30)
    {
        var (lat, lon) = Along(fraction);
        var (aheadLat, aheadLon) = Along(fraction + 0.01);
        var (behindLat, behindLon) = Along(fraction - 0.01);
        var cos = Math.Cos(lat * Math.PI / 180);
        // The direction the road runs here, then a step at right angles to it: that is how far from the road the vehicle really is.
        var east = (aheadLon - behindLon) * cos;
        var north = aheadLat - behindLat;
        var length = Math.Sqrt(east * east + north * north);
        var (perpEast, perpNorth) = (north / length, -east / length);
        return At(lat + perpNorth * km / 111.195, lon + perpEast * km / (111.195 * cos), minutesAgo, speed);
    }

    public async Task<TrackingSessionDto> StartAsync(HttpClient? by = null, string? clientKey = null)
    {
        var response = await (by ?? Driver).PostJsonAsync("/api/v1/mobile/tracking/start", new StartTrackingRequest(TripReference, DeviceId, "Ramesh Yadav", VehicleReference, clientKey));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TrackingSessionDto>();
    }

    public async Task<TrackingSessionDto> StopAsync(bool completed = true, string? clientKey = null, HttpClient? by = null)
    {
        var response = await (by ?? Driver).PostJsonAsync("/api/v1/mobile/tracking/stop", new StopTrackingRequest(TripReference, DeviceId, null, completed, clientKey));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TrackingSessionDto>();
    }

    public Task<HttpResponseMessage> SendRawAsync(IEnumerable<LocationPoint> points, HttpClient? by = null, string? deviceId = null) =>
        (by ?? Driver).PostJsonAsync("/api/v1/mobile/tracking/location/batch", new LocationBatch(TripReference, deviceId ?? DeviceId, [.. points], "1.0.0", 80, "Wifi"));

    public async Task<BatchResult> SendAsync(params LocationPoint[] points)
    {
        var response = await SendRawAsync(points);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BatchResult>();
    }

    public async Task<Guid> TrackedIdAsync(HttpClient? by = null)
    {
        var page = await (by ?? Admin).GetAsync($"/api/v1/tracking/shipments?search={TripReference}");
        page.StatusCode.ShouldBe(HttpStatusCode.OK, await page.Content.ReadAsStringAsync());
        return (await page.ReadAsync<Tms.SharedKernel.Paging.PagedResult<TrackedShipmentSummaryDto>>()).Items.Single().Id;
    }

    public async Task<TrackedShipmentDto> ShipmentAsync(HttpClient? by = null)
    {
        var id = await TrackedIdAsync(by);
        return await (await (by ?? Admin).GetAsync($"/api/v1/tracking/shipments/{id}")).ReadAsync<TrackedShipmentDto>();
    }

    public async Task<T> GetAsync<T>(string tail, HttpClient? by = null)
    {
        var id = await TrackedIdAsync(by);
        var response = await (by ?? Admin).GetAsync($"/api/v1/tracking/shipments/{id}/{tail}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    public async Task<List<TimelineEntryDto>> TimelineAsync() => await GetAsync<List<TimelineEntryDto>>("timeline");

    public async Task<List<AlertDto>> AlertsAsync()
    {
        var id = await TrackedIdAsync();
        return (await (await Admin.GetAsync($"/api/v1/tracking/alerts?shipmentId={id}&pageSize=100")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<AlertDto>>()).Items.ToList();
    }

    public void Dispose()
    {
    }
}
