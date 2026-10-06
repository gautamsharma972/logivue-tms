using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.SharedKernel.Contracts;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TrackingMobileApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_driver_starts_tracking_and_is_told_how_often_to_report_and_when_tracking_goes_stale()
    {
        using var s = await TrackingScenario.CreateAsync(factory);

        var session = await s.StartAsync();

        session.Status.ShouldBe(TrackingSessionStatus.Active);
        session.TripReference.ShouldBe(s.TripReference);
        session.Reference.ShouldStartWith("TS-");
        session.IntervalSeconds.ShouldBe(180); // the default, from settings, not from the app
        (session.StaleAfterMinutes, session.LostAfterMinutes).ShouldBe((10, 30));

        var shipment = await s.ShipmentAsync();
        shipment.Summary.Execution.ShouldBe(ExecutionStatus.EnRouteToOrigin);
        shipment.Summary.Tracking.ShouldBe(TrackingHealth.Healthy);
        shipment.Stops.Count.ShouldBe(2);
        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.TrackingStarted && e.Kind == "Actual");
    }

    [Fact]
    public async Task Starting_twice_with_the_same_key_gives_the_same_session_and_does_not_start_another()
    {
        using var s = await TrackingScenario.CreateAsync(factory);

        var first = await s.StartAsync(clientKey: "start-1");
        var second = await s.StartAsync(clientKey: "start-1");
        var other = await s.StartAsync(clientKey: "start-2"); // a different key on the same running session

        second.SessionId.ShouldBe(first.SessionId);
        other.SessionId.ShouldBe(first.SessionId); // there is one session per trip at a time
    }

    [Fact]
    public async Task Locations_cannot_be_sent_before_tracking_has_started()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        // The trip is known to Tracking once someone looks it up or starts it; here nobody has, so it does not exist yet.
        var response = await s.SendRawAsync([s.Fix(0.1, 2)]);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await s.StartAsync();
        await s.StopAsync();
        var stopped = await s.SendRawAsync([s.Fix(0.2, 1)]);
        stopped.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stopped.Content.ReadAsStringAsync()).ShouldContain("tracking.not_started");
    }

    [Fact]
    public async Task A_batch_of_locations_is_stored_and_the_current_position_is_the_latest_by_gps_time()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        var result = await s.SendAsync(s.Fix(0.18, 6), s.Fix(0.10, 12), s.Fix(0.14, 9)); // out of order on purpose

        result.Accepted.ShouldBe(3);
        result.Rejected.ShouldBeEmpty();
        var current = await s.GetAsync<CurrentLocationDto>("current-location");
        var expected = TrackingScenario.Along(0.18);
        current.Latitude.ShouldBe(expected.Lat, 0.0005);
        current.Longitude.ShouldBe(expected.Lon, 0.0005);
        (DateTimeOffset.UtcNow - current.LastCapturedAt).TotalMinutes.ShouldBeInRange(5, 7); // GPS time, not the moment of upload
        current.LastReceivedAt.ShouldBeGreaterThan(current.LastCapturedAt); // received time is its own fact
        current.Health.ShouldBe(TrackingHealth.Healthy);
    }

    [Fact]
    public async Task Sending_the_same_locations_again_creates_no_duplicates()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        var points = new[] { s.Fix(0.1, 9), s.Fix(0.15, 6), s.Fix(0.2, 3) };

        var first = await s.SendAsync(points);
        var again = await s.SendAsync(points);

        first.Accepted.ShouldBe(3);
        again.Accepted.ShouldBe(0);
        again.Duplicates.ShouldBe(3);
        again.Processed.ShouldAllBe(p => p.Status == "Duplicate");
        var trail = await s.GetAsync<PagedResult<LocationDto>>("locations?includeSuspicious=true");
        trail.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Points_that_cannot_be_real_are_rejected_one_by_one_and_the_rest_are_kept()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        var result = await s.SendAsync(
            s.Fix(0.1, 20), s.At(95, 73, 15), s.At(19, 200, 14), s.At(19, 73, 13, accuracy: 9000), s.At(19.0, 73.0, -45), s.At(19.0, 73.0, 60 * 24 * 9), s.Fix(0.12, 10));

        result.Accepted.ShouldBe(2);
        result.Rejected.Count.ShouldBe(5);
        result.Rejected.ShouldAllBe(r => r.Reason.Length > 0);
        (await s.GetAsync<PagedResult<LocationDto>>("locations?includeSuspicious=true")).TotalCount.ShouldBe(2); // refused points are not stored
    }

    [Fact]
    public async Task A_jump_across_the_country_is_kept_as_suspicious_and_never_moves_the_vehicle()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 10));

        var result = await s.SendAsync(s.At(28.6139, 77.2090, 8)); // Delhi, two minutes later

        result.Suspicious.ShouldBe(1);
        result.Processed.Single().Status.ShouldBe("Suspicious");
        var current = await s.GetAsync<CurrentLocationDto>("current-location");
        current.Latitude.ShouldBeLessThan(20); // still near Mumbai
        var all = await s.GetAsync<PagedResult<LocationDto>>("locations?includeSuspicious=true");
        all.Items.ShouldContain(l => l.Validation == LocationValidation.Suspicious && l.Anomalies.HasFlag(LocationAnomaly.LargeJump));
        (await s.GetAsync<PagedResult<LocationDto>>("locations")).Items.ShouldAllBe(l => l.Validation == LocationValidation.Valid);
        (await s.AlertsAsync()).ShouldContain(a => a.Type == AlertType.GpsAnomaly);
    }

    [Fact]
    public async Task A_late_batch_after_the_phone_was_offline_fills_in_history_without_rewinding_the_vehicle()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.50, 2)); // the live point arrived first

        var result = await s.SendAsync(s.Fix(0.20, 30), s.Fix(0.30, 20), s.Fix(0.40, 10)); // the queued ones come later

        result.Late.ShouldBe(3);
        result.Processed.ShouldAllBe(p => p.Status == "Late");
        var current = await s.GetAsync<CurrentLocationDto>("current-location");
        current.Latitude.ShouldBe(TrackingScenario.Along(0.50).Lat, 0.0005);
        (await s.GetAsync<PagedResult<LocationDto>>("locations")).TotalCount.ShouldBe(4); // all of it is history
    }

    [Fact]
    public async Task Distance_travelled_and_progress_are_worked_out_along_the_planned_route()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.0, 240), s.Fix(0.25, 180), s.Fix(0.5, 120), s.Fix(0.75, 60));

        var shipment = await s.ShipmentAsync();
        shipment.TravelledKm.ShouldBeGreaterThan(80);
        shipment.Summary.ProgressPct.ShouldNotBeNull();
        shipment.Summary.ProgressPct.Value.ShouldBeInRange(60, 90);
        shipment.Summary.RemainingKm.ShouldNotBeNull();
        shipment.Summary.RemainingKm.Value.ShouldBeInRange(15, 60);
        shipment.Summary.OnRoute.ShouldBeTrue();
    }

    [Fact]
    public async Task Stopping_ends_tracking_for_good_and_repeating_the_stop_is_harmless()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.1, 5));

        var stopped = await s.StopAsync(clientKey: "stop-1");
        var again = await s.StopAsync(clientKey: "stop-1");
        var anotherKey = await s.StopAsync(clientKey: "stop-2");

        stopped.Status.ShouldBe(TrackingSessionStatus.Completed);
        again.SessionId.ShouldBe(stopped.SessionId);
        anotherKey.Status.ShouldBe(TrackingSessionStatus.Completed);
        var shipment = await s.ShipmentAsync();
        shipment.Summary.Tracking.ShouldBe(TrackingHealth.Completed);
        shipment.Summary.Execution.ShouldBe(ExecutionStatus.Completed);
        shipment.CurrentSessionId.ShouldBeNull();
        factory.Services.GetRequiredService<TrackingEventLog>().Of<TrackingStopped>(s.TripReference).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Stopping_without_finishing_the_trip_lets_it_be_started_again()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        var stopped = await s.StopAsync(completed: false);
        var restarted = await s.StartAsync();

        stopped.Status.ShouldBe(TrackingSessionStatus.Cancelled);
        restarted.Status.ShouldBe(TrackingSessionStatus.Active);
        restarted.SessionId.ShouldNotBe(stopped.SessionId);
    }

    [Fact]
    public async Task The_sync_call_replays_what_the_phone_saved_offline_and_judges_each_command_alone()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        var request = new SyncRequest(s.DeviceId,
        [
            new SyncCommand("c1", "start", Start: new StartTrackingRequest(s.TripReference, s.DeviceId, "Ramesh", s.VehicleReference)),
            new SyncCommand("c2", "locations", Locations: new LocationBatch(s.TripReference, s.DeviceId, [s.Fix(0.1, 9), s.Fix(0.15, 6)])),
            new SyncCommand("c3", "locations", Locations: new LocationBatch("SH-UNKNOWN", s.DeviceId, [s.Fix(0.1, 3)])),
            new SyncCommand("c4", "status", Status: new DeviceStatusRequest(s.DeviceId, s.TripReference, "Denied", 40, "Offline")),
            new SyncCommand("c5", "dance"),
        ]);

        var response = await s.Driver.PostJsonAsync("/api/v1/mobile/tracking/sync", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var results = await response.ReadAsync<List<SyncCommandResult>>();

        results.Select(r => (r.ClientKey, r.Ok)).ShouldBe([("c1", true), ("c2", true), ("c3", false), ("c4", true), ("c5", false)]);
        results[2].Code.ShouldBe("tracking.shipment_not_found");
        (await s.GetAsync<CurrentLocationDto>("current-location")).ShouldNotBeNull();
        (await s.AlertsAsync()).ShouldContain(a => a.Type == AlertType.GpsUnavailable); // the phone said it cannot read its location
    }

    [Fact]
    public async Task Another_device_can_take_over_a_trip_and_the_change_is_recorded()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.1, 5));

        var response = await s.SendRawAsync([s.Fix(0.15, 2)], deviceId: "SECOND-PHONE");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.TimelineAsync()).ShouldContain(e => e.Type == "DeviceChanged");
        (await s.GetAsync<TrackingHealthDto>("health")).DeviceId.ShouldBe("SECOND-PHONE");
    }

    [Fact]
    public async Task The_driver_sees_the_trips_of_their_own_company_with_their_tracking_state()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        var mine = await (await s.Driver.GetAsync("/api/v1/mobile/tracking/trips")).ReadAsync<List<MobileTripDto>>();
        var rivals = await (await s.Rival.GetAsync("/api/v1/mobile/tracking/trips")).ReadAsync<List<MobileTripDto>>();

        var trip = mine.Single(t => t.TripReference == s.TripReference);
        trip.Session!.Status.ShouldBe(TrackingSessionStatus.Active);
        trip.CanStart.ShouldBeFalse();
        trip.Stops.Count.ShouldBe(2);
        rivals.ShouldNotContain(t => t.TripReference == s.TripReference);
    }
}
