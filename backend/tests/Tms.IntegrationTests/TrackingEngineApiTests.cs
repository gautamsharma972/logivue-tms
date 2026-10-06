using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TrackingEngineApiTests(TmsApiFactory factory)
{
    private TrackingEventLog Log => factory.Services.GetRequiredService<TrackingEventLog>();

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var i = 0; i < 40 && !done(value); i++)
        {
            await Task.Delay(250);
            value = await read();
        }

        return value;
    }

    // ---- the journey: milestones, geofences, stops

    [Fact]
    public async Task Arriving_at_and_leaving_the_pickup_is_seen_from_the_gps_and_confirmed_before_it_is_announced()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.0, 300, speed: 0));
        (await s.ShipmentAsync()).Stops[0].Status.ShouldBe(StopStatus.Pending); // one fix inside is not an arrival

        await s.SendAsync(s.Fix(0.0, 297, speed: 0));
        var arrived = await s.ShipmentAsync();
        arrived.Stops[0].Status.ShouldBe(StopStatus.Arrived);
        arrived.Stops[0].ArrivedAt!.Value.ShouldBe(DateTimeOffset.UtcNow.AddMinutes(-300), TimeSpan.FromMinutes(1)); // timed from the first fix inside
        arrived.Summary.Execution.ShouldBe(ExecutionStatus.ArrivedOrigin);

        await s.SendAsync(s.Fix(0.02, 270), s.Fix(0.04, 267));
        var left = await s.ShipmentAsync();
        left.Stops[0].Status.ShouldBe(StopStatus.Departed);
        left.Stops[0].DepartedAt.ShouldNotBeNull();
        left.Summary.Execution.ShouldBe(ExecutionStatus.InTransit);

        var timeline = await s.TimelineAsync();
        timeline.ShouldContain(e => e.Type == ShipmentEventTypes.ArrivedStop && e.Kind == "Actual" && e.Source == "Gps");
        timeline.ShouldContain(e => e.Type == ShipmentEventTypes.DepartedStop && e.Kind == "Actual");
        timeline.ShouldContain(e => e.Kind == "Planned"); // what was intended is shown beside what happened
        (await EventuallyAsync(() => Task.FromResult(Log.Of<TrackingVehicleArrived>(s.TripReference).ToList()), l => l.Count > 0)).ShouldContain(e => e.StopKind == "Pickup");
        (await EventuallyAsync(() => Task.FromResult(Log.Of<TrackingVehicleDeparted>(s.TripReference).ToList()), l => l.Count > 0)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Reaching_the_destination_tells_delivery_and_the_carrier_scorecard_and_is_in_the_eta_accuracy_report()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.0, 300, speed: 0), s.Fix(0.0, 297, speed: 0), s.Fix(0.02, 270), s.Fix(0.04, 267), s.Fix(0.5, 150));
        await s.SendAsync(s.Fix(0.95, 20));
        var approaching = await s.ShipmentAsync();
        approaching.Stops[1].Status.ShouldBe(StopStatus.Approaching);
        approaching.Summary.Execution.ShouldBe(ExecutionStatus.ApproachingDestination);

        await s.SendAsync(s.Fix(1.0, 8, speed: 0), s.Fix(1.0, 5, speed: 0));
        var arrived = await s.ShipmentAsync();
        arrived.Stops[1].Status.ShouldBe(StopStatus.Arrived);
        arrived.Summary.Execution.ShouldBe(ExecutionStatus.ArrivedDestination);

        var site = await EventuallyAsync(() => Task.FromResult(Log.Events.OfType<DeliveryTrackingEvent>().Where(e => e.TripReference == s.TripReference).ToList()), l => l.Count >= 2);
        site.Select(e => e.Kind).ShouldBe(["EnteredSite", "ArrivedAtSite"], ignoreOrder: true);
        site.ShouldAllBe(e => e.OrderId != null); // the drop's order, so Delivery knows which delivery it is

        var performance = await EventuallyAsync(() => Task.FromResult(Log.Events.OfType<TrackingPerformanceEvent>().Where(e => e.TripReference == s.TripReference).ToList()), l => l.Count >= 2);
        performance.ShouldContain(e => e.Kind == "PickupDelay");
        performance.ShouldContain(e => e.Kind == "DeliveryDelay");

        var report = await s.Admin.GetAsync($"/api/v1/tracking/reports/eta-accuracy?format=csv&leadHours=2&from={DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}");
        report.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csv = await report.Content.ReadAsStringAsync();
        csv.ShouldContain("Within 15 min %");
        csv.ShouldContain(s.TripReference);
    }

    [Fact]
    public async Task A_shared_restricted_area_is_announced_and_alerted_when_a_vehicle_enters_it()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        var (lat, lon) = TrackingScenario.Along(0.5);
        var code = $"RZ{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var created = await s.Admin.PostJsonAsync("/api/v1/tracking/geofences", new SaveGeofenceRequest(code, "Ghat restricted zone", GeofenceType.RestrictedArea, lat, lon, 600, null, null, null, null, null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        await s.StartAsync();

        await s.SendAsync(s.At(lat, lon, 30, 20), s.At(lat, lon, 26, 20));

        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.EnteredGeofence && e.Label.Contains("Ghat restricted zone"));
        (await s.AlertsAsync()).ShouldContain(a => a.Type == AlertType.GeofenceException);
        (await EventuallyAsync(() => Task.FromResult(Log.Of<EnteredGeofence>(s.TripReference).ToList()), l => l.Count > 0)).ShouldContain(e => e.GeofenceCode == code);
    }

    // ---- tracking health

    [Fact]
    public async Task Tracking_that_goes_quiet_becomes_stale_then_lost_and_never_claims_the_vehicle_has_stopped()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 15)); // the last word was a quarter of an hour ago

        var stale = await s.ShipmentAsync(); // reading it is what notices
        stale.Summary.Tracking.ShouldBe(TrackingHealth.Stale);
        stale.SessionStatus.ShouldBe(TrackingSessionStatus.Stale);
        (await s.AlertsAsync()).ShouldContain(a => a.Type == AlertType.TrackingStale && a.Message.Contains("unavailable"));
        (await s.GetAsync<CurrentLocationDto>("current-location")).Health.ShouldBe(TrackingHealth.Stale);

        using var lost = await TrackingScenario.CreateAsync(factory);
        await lost.StartAsync();
        await lost.SendAsync(lost.Fix(0.10, 45));
        var shipment = await lost.ShipmentAsync();

        shipment.Summary.Tracking.ShouldBe(TrackingHealth.Lost);
        var alerts = await lost.AlertsAsync();
        var alert = alerts.Single(a => a.Type == AlertType.TrackingLost);
        alert.Message.ShouldContain("Vehicle location unavailable since");
        alert.Message.ToLowerInvariant().ShouldNotContain("stopped");
        alert.ExceptionId.ShouldNotBeNull(); // lost tracking needs an owner
        (await lost.GetAsync<TrackingHealthDto>("health")).Gaps.ShouldContain(g => g.GapEnd == null);
        (await EventuallyAsync(() => Task.FromResult(Log.Of<TrackingLost>(lost.TripReference).ToList()), l => l.Count > 0)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task When_locations_come_back_the_gap_is_closed_and_the_alerts_clear_but_the_exception_stays_for_a_person()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 45));
        (await s.ShipmentAsync()).Summary.Tracking.ShouldBe(TrackingHealth.Lost);

        await s.SendAsync(s.Fix(0.15, 1));

        var shipment = await s.ShipmentAsync();
        shipment.Summary.Tracking.ShouldBe(TrackingHealth.Healthy);
        shipment.SessionStatus.ShouldBe(TrackingSessionStatus.Active);
        var health = await s.GetAsync<TrackingHealthDto>("health");
        health.Gaps.ShouldAllBe(g => g.GapEnd != null);
        health.Gaps.ShouldContain(g => g.DurationMinutes >= 40);
        (await s.AlertsAsync()).Where(a => a.Type is AlertType.TrackingLost or AlertType.TrackingStale).ShouldAllBe(a => a.Status == AlertStatus.Resolved);
        var exceptions = await s.GetAsync<List<TrackingExceptionSummaryDto>>("exceptions");
        exceptions.Single(e => e.Type == AlertType.TrackingLost).ConditionCleared.ShouldBeTrue();
        exceptions.Single(e => e.Type == AlertType.TrackingLost).Status.ShouldBe(ExceptionStatus.Open); // somebody still has to look at why
        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.TrackingResumed);
    }

    // ---- route

    [Fact]
    public async Task A_short_detour_is_not_a_deviation()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.30, 120), s.OffRoute(0.32, 4.5, 14), s.OffRoute(0.32, 4.5, 11), s.Fix(0.33, 7)); // off the road for only six minutes

        var route = await s.GetAsync<RouteDto>("route");
        route.Deviations.ShouldBeEmpty();
        (await s.AlertsAsync()).ShouldNotContain(a => a.Type == AlertType.RouteDeviation);
    }

    [Fact]
    public async Task Staying_off_the_planned_route_raises_a_deviation_an_alert_and_an_exception_and_returning_resolves_the_first_two()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.30, 120), s.OffRoute(0.32, 3.8, 30), s.OffRoute(0.32, 3.8, 24), s.OffRoute(0.32, 3.8, 18));

        var during = await s.GetAsync<RouteDto>("route");
        var deviation = during.Deviations.ShouldHaveSingleItem();
        deviation.Status.ShouldBe(DeviationStatus.Open);
        deviation.DistanceFromRouteKm.ShouldBeInRange(3.5, 4.0);
        deviation.DurationMinutes.ShouldBeGreaterThanOrEqualTo(10);
        deviation.Severity.ShouldBe(Severity.High);
        (await s.ShipmentAsync()).Summary.OnRoute.ShouldBeFalse();
        var alert = (await s.AlertsAsync()).Single(a => a.Type == AlertType.RouteDeviation);
        alert.Severity.ShouldBe(Severity.High);
        alert.ExceptionId.ShouldNotBeNull();
        (await s.GetAsync<List<TrackingExceptionSummaryDto>>("exceptions")).ShouldContain(e => e.Type == AlertType.RouteDeviation && e.Status == ExceptionStatus.Open);
        (await EventuallyAsync(() => Task.FromResult(Log.Of<RouteDeviationDetected>(s.TripReference).ToList()), l => l.Count > 0)).ShouldNotBeEmpty();

        await s.SendAsync(s.Fix(0.33, 12), s.Fix(0.34, 6));

        var after = await s.GetAsync<RouteDto>("route");
        after.Deviations.Single().Status.ShouldBe(DeviationStatus.Resolved);
        (await s.AlertsAsync()).Single(a => a.Type == AlertType.RouteDeviation).Status.ShouldBe(AlertStatus.Resolved);
        (await s.GetAsync<List<TrackingExceptionSummaryDto>>("exceptions")).Single(e => e.Type == AlertType.RouteDeviation).ConditionCleared.ShouldBeTrue();
        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.RouteResumed);
        (await EventuallyAsync(() => Task.FromResult(Log.Of<RouteDeviationResolved>(s.TripReference).ToList()), l => l.Count > 0)).ShouldNotBeEmpty();

        var reason = await s.Admin.PostJsonAsync($"/api/v1/tracking/deviations/{deviation.Id}/reason", new DeviationReasonRequest(DelayReason.RoadClosure, "Diversion at the toll plaza"));
        reason.StatusCode.ShouldBe(HttpStatusCode.OK, await reason.Content.ReadAsStringAsync());
        (await reason.ReadAsync<RouteDeviationDto>()).Reason.ShouldBe(DelayReason.RoadClosure);
    }

    // ---- dwell

    [Fact]
    public async Task Staying_far_longer_than_expected_at_the_customer_is_excess_dwell()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.95, 90));

        await s.SendAsync(s.Fix(1.0, 60, speed: 0), s.Fix(1.0, 45, speed: 0), s.Fix(1.0, 30, speed: 0), s.Fix(1.0, 14, speed: 0));

        var alert = (await s.AlertsAsync()).Single(a => a.Type == AlertType.ExcessiveDwell);
        alert.Message.ShouldContain("over the 30 min expected");
        var analytics = await s.GetAsync<JourneyAnalyticsDto>("analytics");
        analytics.Dwells.ShouldContain(d => d.Kind == DwellKind.PlannedStop && d.ExcessDurationMinutes >= 15 && d.Status == DwellStatus.Ongoing);
        (await EventuallyAsync(() => Task.FromResult(Log.Of<ExcessiveDwellDetected>(s.TripReference).ToList()), l => l.Count > 0)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_long_stop_in_the_middle_of_nowhere_is_an_unplanned_stop()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.5, 70, speed: 0), s.Fix(0.5, 55, speed: 0), s.Fix(0.5, 40, speed: 0), s.Fix(0.5, 25, speed: 0));

        (await s.AlertsAsync()).ShouldContain(a => a.Type == AlertType.UnplannedStop);
        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.UnplannedStop);
        (await s.GetAsync<JourneyAnalyticsDto>("analytics")).UnplannedStops.ShouldBe(1);
        (await s.GetAsync<List<TrackingExceptionSummaryDto>>("exceptions")).ShouldBeEmpty(); // a warning, not an exception, by the default rules
    }

    // ---- arrival estimates and risk

    [Theory]
    [InlineData(300, RiskStatus.OnTime, 0)]
    [InlineData(60, RiskStatus.AtRisk, 0)]
    [InlineData(35, RiskStatus.Delayed, 1)]
    [InlineData(5, RiskStatus.SeverelyDelayed, 1)]
    public async Task How_late_the_vehicle_will_be_decides_the_risk_the_alerts_and_whether_an_exception_is_needed(int plannedInMinutes, RiskStatus expected, int exceptions)
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(plannedInMinutes));
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.49, 4), s.Fix(0.5, 2));

        var shipment = await s.ShipmentAsync();
        shipment.Summary.Risk.ShouldBe(expected);
        shipment.Summary.EtaAt.ShouldNotBeNull();
        shipment.Summary.EtaConfidence!.Value.ShouldBeInRange(0.2, 0.95); // a rule-based estimate never claims certainty
        var alerts = await s.AlertsAsync();
        alerts.Count(a => a.Type is AlertType.EtaAtRisk or AlertType.EtaDelayed or AlertType.DeliverySlaRisk && a.Status != AlertStatus.Resolved).ShouldBe(expected == RiskStatus.OnTime ? 0 : 1);
        (await s.GetAsync<List<TrackingExceptionSummaryDto>>("exceptions")).Count(e => e.Type is AlertType.EtaDelayed or AlertType.DeliverySlaRisk).ShouldBe(exceptions);
        if (expected == RiskStatus.SeverelyDelayed)
        {
            alerts.Single(a => a.Type == AlertType.DeliverySlaRisk).Severity.ShouldBe(Severity.Critical);
        }
    }

    [Fact]
    public async Task Estimates_are_kept_for_every_stop_and_the_history_shows_how_they_changed()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(200));
        await s.StartAsync();

        await s.SendAsync(s.Fix(0.2, 60));
        await s.SendAsync(s.Fix(0.3, 30));
        await s.SendAsync(s.Fix(0.5, 2));

        var eta = await s.GetAsync<EtaDto>("eta");
        eta.CalculationVersion.ShouldNotBeNullOrEmpty();
        eta.Stops.Count.ShouldBe(2);
        eta.Stops.Single(x => x.Kind == StopKind.Drop).EtaAt.ShouldNotBeNull();
        eta.Stops.Single(x => x.Kind == StopKind.Pickup).EtaAt.ShouldBeNull(); // already behind the vehicle: nothing to estimate
        eta.History.Count.ShouldBeGreaterThanOrEqualTo(2);
        eta.History.ShouldAllBe(h => h.IsFinalDestination);
        eta.History.Select(h => h.RemainingKm).Distinct().Count().ShouldBeGreaterThan(1); // it came down as the vehicle drove
        eta.PlannedAt.ShouldNotBeNull();
        eta.Overridden.ShouldBeFalse();
    }

    [Fact]
    public async Task An_operator_can_correct_the_estimate_with_a_reason_and_the_calculation_underneath_is_kept()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(200));
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.49, 4), s.Fix(0.5, 2));
        var id = await s.TrackedIdAsync();
        var before = await s.GetAsync<EtaDto>("eta");
        var corrected = DateTimeOffset.UtcNow.AddHours(6);

        (await s.Admin.PostJsonAsync($"/api/v1/tracking/shipments/{id}/eta/override", new OverrideEtaRequest(corrected, " "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var response = await s.Admin.PostJsonAsync($"/api/v1/tracking/shipments/{id}/eta/override", new OverrideEtaRequest(corrected, "Road closure reported by the driver"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var after = await response.ReadAsync<EtaDto>();

        after.Overridden.ShouldBeTrue();
        after.EtaAt!.Value.ShouldBe(corrected, TimeSpan.FromSeconds(1));
        after.SystemEtaAt!.Value.ShouldBe(before.SystemEtaAt!.Value, TimeSpan.FromMinutes(5)); // what the system worked out is still there
        after.OverrideReason.ShouldBe("Road closure reported by the driver");
        after.Risk.ShouldBeOneOf(RiskStatus.Delayed, RiskStatus.SeverelyDelayed); // judged against the corrected time
        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.EtaOverride && e.Source == "Manual" && (e.Reason ?? string.Empty).Contains("Road closure", StringComparison.Ordinal));

        var cleared = await (await s.Admin.DeleteAsync($"/api/v1/tracking/shipments/{id}/eta/override")).ReadAsync<EtaDto>();
        cleared.Overridden.ShouldBeFalse();
        cleared.EtaAt!.Value.ShouldBe(cleared.SystemEtaAt!.Value);
    }

    [Fact]
    public async Task A_milestone_recorded_by_hand_needs_a_reason_and_shows_on_the_timeline_as_manual()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        var shipment = await s.ShipmentAsync();
        var id = shipment.Summary.Id;

        (await s.Admin.PostJsonAsync($"/api/v1/tracking/shipments/{id}/milestones", new ManualMilestoneRequest(MilestoneType.ArrivedOrigin, shipment.Stops[0].Id, null, ""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var response = await s.Admin.PostJsonAsync($"/api/v1/tracking/shipments/{id}/milestones", new ManualMilestoneRequest(MilestoneType.ArrivedOrigin, shipment.Stops[0].Id, DateTimeOffset.UtcNow.AddMinutes(-20), "Driver called from the gate: no GPS signal in the yard"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await s.ShipmentAsync()).Stops[0].Status.ShouldBe(StopStatus.Arrived);
        (await s.TimelineAsync()).ShouldContain(e => e.Type == ShipmentEventTypes.ManualMilestone && e.Source == "Manual");
        var reason = await s.Admin.PostJsonAsync($"/api/v1/tracking/shipments/{id}/delay-reason", new SetDelayReasonRequest(DelayReason.WarehouseDelay, "Dock was full"));
        reason.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reason.ReadAsync<TrackedShipmentDto>()).DelayReason.ShouldBe(DelayReason.WarehouseDelay);
    }

    // ---- control tower, vehicles

    [Fact]
    public async Task The_control_tower_counts_trips_by_how_they_are_doing_and_lists_the_worst_first()
    {
        using var onTime = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(300));
        using var late = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(5));
        using var quiet = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(300));
        foreach (var t in new[] { onTime, late, quiet })
        {
            await t.StartAsync();
        }

        await onTime.SendAsync(onTime.Fix(0.49, 4), onTime.Fix(0.5, 2));
        await late.SendAsync(late.Fix(0.49, 4), late.Fix(0.5, 2));
        await quiet.SendAsync(quiet.Fix(0.1, 50));

        // Each trip belongs to its own carrier, so a carrier's own figures are exactly its own trip.
        var mine = await (await onTime.Admin.GetAsync($"/api/v1/control-tower/summary?transporterId={onTime.Transporter.Id}")).ReadAsync<ControlTowerSummaryDto>();
        mine.Active.ShouldBe(1);
        mine.OnTime.ShouldBe(1);

        var lateSummary = await (await late.Admin.GetAsync($"/api/v1/control-tower/summary?transporterId={late.Transporter.Id}")).ReadAsync<ControlTowerSummaryDto>();
        lateSummary.Delayed.ShouldBe(1);
        lateSummary.OpenExceptions.ShouldBeGreaterThanOrEqualTo(1);

        var quietSummary = await (await quiet.Admin.GetAsync($"/api/v1/control-tower/summary?transporterId={quiet.Transporter.Id}")).ReadAsync<ControlTowerSummaryDto>();
        quietSummary.TrackingLost.ShouldBe(1);

        var list = await (await onTime.Admin.GetAsync("/api/v1/control-tower/shipments?activeOnly=true&pageSize=100")).ReadAsync<PagedResult<TrackedShipmentSummaryDto>>();
        var order = list.Items.Select(i => i.TripReference).ToList();
        order.IndexOf(late.TripReference).ShouldBeLessThan(order.IndexOf(onTime.TripReference)); // the late one is nearer the top
        order.IndexOf(quiet.TripReference).ShouldBeLessThan(order.IndexOf(onTime.TripReference));
        (await (await onTime.Admin.GetAsync("/api/v1/control-tower/map")).ReadAsync<List<TrackedShipmentSummaryDto>>()).ShouldContain(m => m.TripReference == onTime.TripReference);
    }

    [Fact]
    public async Task A_vehicle_can_be_found_by_its_registration_with_its_trip_and_history()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 12), s.Fix(0.14, 9), s.Fix(0.18, 6));

        var found = await (await s.Admin.GetAsync($"/api/v1/tracking/vehicles?search={s.VehicleReference}")).ReadAsync<PagedResult<VehicleTrackingDto>>();
        var vehicle = found.Items.ShouldHaveSingleItem();
        vehicle.ShipmentReference.ShouldBe(s.TripReference);
        vehicle.Position.Health.ShouldBe(TrackingHealth.Healthy);
        vehicle.Position.DriverName.ShouldBe("Ramesh Yadav");
        vehicle.TodayKm.ShouldBeGreaterThan(5);

        (await (await s.Admin.GetAsync($"/api/v1/tracking/vehicles/{s.VehicleReference}/current")).ReadAsync<VehicleTrackingDto>()).Position.VehicleReference.ShouldBe(s.VehicleReference);
        var history = await (await s.Admin.GetAsync($"/api/v1/tracking/vehicles/{s.VehicleReference}/history?maxPoints=2")).ReadAsync<List<LocationDto>>();
        history.Count.ShouldBe(2);
        history[0].CapturedAt.ShouldBeLessThan(history[1].CapturedAt);
        (await s.Rival.GetAsync($"/api/v1/tracking/vehicles/{s.VehicleReference}/current")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- alerts and exceptions

    [Fact]
    public async Task An_exception_is_acknowledged_assigned_escalated_resolved_with_a_cause_and_closed()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 45));
        await s.ShipmentAsync();
        var exception = (await s.GetAsync<List<TrackingExceptionSummaryDto>>("exceptions")).Single(e => e.Type == AlertType.TrackingLost);
        var id = exception.Id;
        exception.Status.ShouldBe(ExceptionStatus.Open);
        exception.Number.ShouldStartWith("TEX-");

        (await s.Admin.PostAsync($"/api/v1/tracking/exceptions/{id}/acknowledge", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Admin.PostJsonAsync($"/api/v1/tracking/exceptions/{id}/assign", new AssignExceptionRequest(null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var assigned = await (await s.Admin.PostJsonAsync($"/api/v1/tracking/exceptions/{id}/assign", new AssignExceptionRequest(Guid.NewGuid(), "Control room", DateTimeOffset.UtcNow.AddHours(2), null))).ReadAsync<TrackingExceptionDto>();
        assigned.Summary.Department.ShouldBe("Control room");
        assigned.DriverPhone.ShouldBe("9876543210"); // so the operator can ring the driver
        assigned.LastLatitude.ShouldNotBeNull(); // and knows where the vehicle was last seen

        (await s.Admin.PostJsonAsync($"/api/v1/tracking/exceptions/{id}/escalate", new EscalateExceptionRequest(" ", null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var escalated = await (await s.Admin.PostJsonAsync($"/api/v1/tracking/exceptions/{id}/escalate", new EscalateExceptionRequest("Driver is not answering", "Transport manager"))).ReadAsync<TrackingExceptionDto>();
        (escalated.Summary.Status, escalated.Summary.EscalationLevel, escalated.Summary.EscalatedTo).ShouldBe((ExceptionStatus.Escalated, 1, "Transport manager"));

        (await s.Admin.PostAsync($"/api/v1/tracking/exceptions/{id}/close", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // resolve first
        (await s.Admin.PostJsonAsync($"/api/v1/tracking/exceptions/{id}/resolve", new ResolveExceptionRequest("", null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var resolved = await (await s.Admin.PostJsonAsync($"/api/v1/tracking/exceptions/{id}/resolve", new ResolveExceptionRequest("Phone battery died", DelayReason.Unknown, "Spoke to the driver"))).ReadAsync<TrackingExceptionDto>();
        resolved.Summary.Status.ShouldBe(ExceptionStatus.Resolved);
        resolved.RootCause.ShouldBe("Phone battery died");
        (await (await s.Admin.PostAsync($"/api/v1/tracking/exceptions/{id}/close", null)).ReadAsync<TrackingExceptionDto>()).Summary.Status.ShouldBe(ExceptionStatus.Closed);
        resolved.Notes.Select(n => n.Text).ShouldContain(t => t.StartsWith("Escalated"));
    }

    [Fact]
    public async Task An_alert_can_be_acknowledged_and_resolved_by_staff_only()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 15));
        await s.ShipmentAsync();
        var alert = (await s.AlertsAsync()).Single(a => a.Type == AlertType.TrackingStale);

        (await s.Driver.PostAsync($"/api/v1/tracking/alerts/{alert.Id}/acknowledge", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await s.Admin.PostAsync($"/api/v1/tracking/alerts/{alert.Id}/acknowledge", null)).ReadAsync<AlertDto>()).Status.ShouldBe(AlertStatus.Acknowledged);
        var resolved = await (await s.Admin.PostJsonAsync($"/api/v1/tracking/alerts/{alert.Id}/resolve", new ResolveAlertRequest("Driver called in"))).ReadAsync<AlertDto>();
        resolved.Status.ShouldBe(AlertStatus.Resolved);
        resolved.ResolutionNote.ShouldBe("Driver called in");
        (await s.Admin.PostAsync($"/api/v1/tracking/alerts/{alert.Id}/acknowledge", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
