using Tms.Modules.Tracking.Domain;

namespace Tms.UnitTests.Tracking;

public class GeofenceEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly GeofenceShape Warehouse = new(new GeoPoint(19.0, 73.0), 200);
    private static readonly GeofenceSetting Rules = new(DefaultRadiusM: 200, MaxAccuracyM: 100, EntryConfirmationSeconds: 60, ExitConfirmationSeconds: 60, MinConfirmationPoints: 2, StayedAfterMinutes: 30);
    private static readonly GeoPoint Inside = new(19.0005, 73.0);
    private static readonly GeoPoint Outside = new(19.02, 73.0); // a couple of km away

    private static GeofenceTransition? Step(PresenceState s, GeoPoint p, int minutes, double? accuracy = 10) => GeofenceEvaluator.Observe(s, Warehouse, p, accuracy, T0.AddMinutes(minutes), Rules);

    [Fact]
    public void Entering_is_announced_only_once_it_has_been_confirmed_and_is_timed_from_the_first_fix_inside()
    {
        var state = new PresenceState();

        Step(state, Inside, 0).ShouldBeNull(); // one point is not enough
        var entered = Step(state, Inside, 3);

        entered.ShouldNotBeNull();
        entered.Type.ShouldBe(GeofenceEventType.Entered);
        entered.At.ShouldBe(T0); // when the vehicle arrived, not when it was confirmed
        state.State.ShouldBe(Presence.Inside);
    }

    [Fact]
    public void Driving_through_a_geofence_without_stopping_is_not_an_arrival()
    {
        var state = new PresenceState();

        Step(state, Inside, 0).ShouldBeNull();
        Step(state, Outside, 3).ShouldBeNull();

        state.State.ShouldBe(Presence.Outside);
    }

    [Fact]
    public void A_vague_fix_cannot_move_the_vehicle_in_or_out()
    {
        var state = new PresenceState();

        Step(state, Inside, 0, accuracy: 400).ShouldBeNull();
        Step(state, Inside, 3, accuracy: 400).ShouldBeNull();

        state.State.ShouldBe(Presence.Outside);
    }

    [Fact]
    public void Leaving_needs_confirming_too_and_a_flicker_outside_does_not_end_the_stay()
    {
        var state = new PresenceState();
        Step(state, Inside, 0);
        Step(state, Inside, 3);

        Step(state, Outside, 10).ShouldBeNull(); // one point outside
        Step(state, Inside, 13).ShouldBeNull(); // back: it never left
        state.State.ShouldBe(Presence.Inside);

        Step(state, Outside, 20).ShouldBeNull();
        var exited = Step(state, Outside, 24);
        exited.ShouldNotBeNull();
        exited.Type.ShouldBe(GeofenceEventType.Exited);
        exited.At.ShouldBe(T0.AddMinutes(20));
    }

    [Fact]
    public void A_long_stay_is_reported_once()
    {
        var state = new PresenceState();
        Step(state, Inside, 0);
        Step(state, Inside, 3);

        Step(state, Inside, 20).ShouldBeNull();
        Step(state, Inside, 40)!.Type.ShouldBe(GeofenceEventType.Stayed);
        Step(state, Inside, 60).ShouldBeNull();
    }

    [Fact]
    public void When_the_trip_ends_a_seen_but_unconfirmed_arrival_is_not_lost()
    {
        var state = new PresenceState();
        Step(state, Inside, 0);

        var flushed = GeofenceEvaluator.Flush(state, T0.AddMinutes(2));

        flushed.ShouldNotBeNull();
        flushed.Type.ShouldBe(GeofenceEventType.Entered);
        flushed.At.ShouldBe(T0);
    }

    [Fact]
    public void A_polygon_is_used_when_there_is_one()
    {
        GeoPoint[] yard = [new(19, 73), new(19, 73.01), new(19.01, 73.01), new(19.01, 73)];
        var shape = new GeofenceShape(new GeoPoint(19.005, 73.005), 20, yard);
        var state = new PresenceState();

        GeofenceEvaluator.Observe(state, shape, new GeoPoint(19.009, 73.009), 10, T0, Rules); // about a kilometre from the centre, inside the yard
        var entered = GeofenceEvaluator.Observe(state, shape, new GeoPoint(19.009, 73.009), 10, T0.AddMinutes(3), Rules);

        entered.ShouldNotBeNull();
        entered.Type.ShouldBe(GeofenceEventType.Entered);
    }
}

public class DeviationTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly RouteSetting Rules = new(DeviationKm: 3, DeviationMinutes: 10, ResolveMinutes: 5, HighKm: 3.5, CriticalKm: 10, CriticalMinutes: 90, MinOffRoutePoints: 2);

    private static DeviationResult Observe(DeviationState state, double offKm, int minutes, double? accuracy = 10) => DeviationTracker.Observe(state, offKm, accuracy, T0.AddMinutes(minutes), Rules);

    [Fact]
    public void A_short_detour_is_not_a_deviation()
    {
        var state = new DeviationState();

        Observe(state, 4.2, 0).Change.ShouldBe(DeviationChange.None);
        Observe(state, 4.0, 5).Change.ShouldBe(DeviationChange.None);
        Observe(state, 0.3, 8).Change.ShouldBe(DeviationChange.None); // back on the road before ten minutes

        state.IsOpen.ShouldBeFalse();
        state.OffRouteSince.ShouldBeNull();
    }

    [Fact]
    public void Staying_off_the_corridor_long_enough_opens_a_deviation_with_the_right_severity()
    {
        var state = new DeviationState();
        Observe(state, 3.8, 0);
        Observe(state, 3.8, 6);

        var opened = Observe(state, 3.8, 12);

        opened.Change.ShouldBe(DeviationChange.Opened);
        opened.Severity.ShouldBe(Severity.High); // 3.8 km is past the 3.5 km line
        opened.DurationMinutes.ShouldBe(12);
        opened.StartedAt.ShouldBe(T0);
    }

    [Fact]
    public void A_small_overshoot_is_a_warning_and_a_long_or_far_one_is_critical()
    {
        DeviationTracker.SeverityFor(3.2, 15, Rules).ShouldBe(Severity.Warning);
        DeviationTracker.SeverityFor(3.8, 15, Rules).ShouldBe(Severity.High);
        DeviationTracker.SeverityFor(12, 15, Rules).ShouldBe(Severity.Critical);
        DeviationTracker.SeverityFor(3.2, 120, Rules).ShouldBe(Severity.Critical);
    }

    [Fact]
    public void Gps_vagueness_is_taken_off_the_distance_before_judging_it()
    {
        var state = new DeviationState();

        // 3.4 km off with a fix only accurate to 800 m is really about 2.6 km: inside the corridor.
        Observe(state, 3.4, 0, accuracy: 800);
        Observe(state, 3.4, 20, accuracy: 800).Change.ShouldBe(DeviationChange.None);
        state.OffPoints.ShouldBe(0);
    }

    [Fact]
    public void A_deviation_is_closed_only_after_the_vehicle_has_been_back_on_the_road_for_a_while()
    {
        var state = new DeviationState();
        Observe(state, 4.5, 0);
        Observe(state, 4.5, 12).Change.ShouldBe(DeviationChange.Opened);

        Observe(state, 0.2, 20).Change.ShouldBe(DeviationChange.None); // back, but only just
        Observe(state, 4.5, 22).Change.ShouldBe(DeviationChange.Updated); // and gone again: still the same deviation

        Observe(state, 0.2, 30);
        var resolved = Observe(state, 0.2, 36);

        resolved.Change.ShouldBe(DeviationChange.Resolved);
        resolved.DurationMinutes.ShouldBe(30);
        state.IsOpen.ShouldBeFalse();
    }
}

public class DwellTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly DwellSetting Rules = new(DwellSetting.DefaultExpected, ExcessThresholdMinutes: 15, StationarySpeedKph: 3, StationaryRadiusM: 100, MinStationaryMinutes: 10, UnplannedStopMinutes: 30);
    private static readonly GeoPoint Here = new(19.0, 73.0);

    private static IReadOnlyList<DwellResult> At(DwellState s, int minutes, string? kind = null, string? name = null, double? speed = 0, GeoPoint? point = null) =>
        DwellTracker.Observe(s, point ?? Here, speed, T0.AddMinutes(minutes), kind, name, Rules);

    [Fact]
    public void A_normal_stop_at_a_known_place_is_recorded_without_alarm()
    {
        var state = new DwellState();
        At(state, 0, "Customer", "ABC");

        var started = At(state, 12, "Customer", "ABC");
        At(state, 25, "Customer", "ABC").ShouldBeEmpty(); // well within the 30 min a customer stop is expected to take

        started.ShouldHaveSingleItem().Change.ShouldBe(DwellChange.Started);
        started[0].Kind.ShouldBe(DwellKind.PlannedStop);
        var ended = At(state, 40, "Customer", "ABC", speed: 30, point: new GeoPoint(19.01, 73.01));
        ended.ShouldHaveSingleItem().Change.ShouldBe(DwellChange.Ended);
        ended[0].DurationMinutes.ShouldBe(25);
    }

    [Fact]
    public void Staying_far_longer_than_expected_is_excess_and_is_reported_once()
    {
        var state = new DwellState();
        At(state, 0, "Customer", "ABC");
        At(state, 12, "Customer", "ABC");

        var excess = At(state, 50, "Customer", "ABC"); // 50 min against 30 expected: 20 over, past the 15 allowed

        excess.ShouldHaveSingleItem().Change.ShouldBe(DwellChange.Excess);
        excess[0].ExcessMinutes.ShouldBe(20);
        excess[0].ExpectedMinutes.ShouldBe(30);
        At(state, 70, "Customer", "ABC").ShouldBeEmpty();
    }

    [Fact]
    public void The_expected_stay_differs_by_kind_of_place()
    {
        Rules.Expected("Warehouse").ShouldBe(60);
        Rules.Expected("Customer").ShouldBe(30);
        Rules.Expected("Hub").ShouldBe(45);
        Rules.Expected("Nowhere in particular").ShouldBe(30);
    }

    [Fact]
    public void Stopped_for_a_long_time_away_from_any_known_place_is_an_unplanned_stop()
    {
        var state = new DwellState();
        At(state, 0);
        At(state, 12).ShouldHaveSingleItem().Kind.ShouldBe(DwellKind.UnplannedStop);

        At(state, 20).ShouldBeEmpty();
        var raised = At(state, 35);

        raised.ShouldHaveSingleItem().Change.ShouldBe(DwellChange.UnplannedStop);
        raised[0].DurationMinutes.ShouldBe(35);
        At(state, 50).ShouldBeEmpty(); // once
    }

    [Fact]
    public void Slowing_in_traffic_is_not_a_stop()
    {
        var state = new DwellState();

        At(state, 0, point: new GeoPoint(19.0, 73.0), speed: 8);
        At(state, 5, point: new GeoPoint(19.01, 73.0), speed: 8); // a kilometre on: moving
        At(state, 12, point: new GeoPoint(19.02, 73.0), speed: 8).ShouldBeEmpty();

        state.Open.ShouldBeFalse();
    }

    [Fact]
    public void When_the_trip_ends_an_open_stop_is_closed_at_the_last_time_the_vehicle_was_seen_still()
    {
        var state = new DwellState();
        At(state, 0, "Warehouse", "Pune DC");
        At(state, 15, "Warehouse", "Pune DC");

        var closed = DwellTracker.Close(state, Rules);

        closed.ShouldNotBeNull();
        closed.DurationMinutes.ShouldBe(15);
        state.Open.ShouldBeFalse();
    }
}

public class EtaCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly EtaSetting Rules = new(AverageSpeedKph: 50, SpeedBlend: 0, OnTimeToleranceMinutes: 10, DelayedAfterMinutes: 30, SeverelyDelayedAfterMinutes: 60);
    private static readonly GeoPoint Here = new(19, 73);

    private static EtaStop Stop(int sequence, double along, DateTimeOffset? planned, int dwell = 0, StopKind kind = StopKind.Drop, StopStatus status = StopStatus.Pending, DateTimeOffset? windowEnd = null, DateTimeOffset? arrived = null) =>
        new(Guid.NewGuid(), sequence, kind, $"Stop {sequence}", null, along, planned, windowEnd, dwell, status, arrived);

    private static EtaInput Input(IReadOnlyList<EtaStop> stops, double vehicleAlong = 0, TrackingHealth health = TrackingHealth.Healthy, bool offRoute = false, double? speed = null, int samples = 5, DateTimeOffset? now = null) =>
        new(now ?? Now, Here, vehicleAlong, true, false, speed, samples, stops, health, offRoute);

    [Fact]
    public void Arrival_is_the_distance_left_at_the_average_speed()
    {
        var eta = EtaCalculator.Calculate(Input([Stop(1, 100, Now.AddHours(3))]), Rules);

        eta.ShouldHaveSingleItem().Eta.ShouldBe(Now.AddHours(2)); // 100 km at 50 km/h
        eta[0].RemainingKm.ShouldBe(100);
        eta[0].DelayMinutes.ShouldBeLessThan(0); // an hour to spare
        eta[0].Risk.ShouldBe(RiskStatus.OnTime);
    }

    [Fact]
    public void The_speed_actually_seen_counts_in_proportion_to_the_blend()
    {
        var blended = Rules with { SpeedBlend = 1 };

        var eta = EtaCalculator.Calculate(Input([Stop(1, 100, null)], speed: 80), blended);

        eta[0].Eta.ShouldBe(Now.AddMinutes(75)); // 100 km at 80 km/h
    }

    [Fact]
    public void The_stay_expected_at_each_stop_pushes_the_later_ones_back()
    {
        var eta = EtaCalculator.Calculate(Input([Stop(1, 50, null, dwell: 30), Stop(2, 100, null)]), Rules);

        eta[0].Eta.ShouldBe(Now.AddHours(1));
        eta[1].Eta.ShouldBe(Now.AddHours(1).AddMinutes(30).AddHours(1)); // the stop, then the next 50 km
    }

    [Fact]
    public void A_vehicle_already_at_a_stop_counts_only_what_is_left_of_its_stay()
    {
        var arrived = Now.AddMinutes(-20);
        var stops = new[] { Stop(1, 0, null, dwell: 30, status: StopStatus.Arrived, arrived: arrived), Stop(2, 50, null) };

        var eta = EtaCalculator.Calculate(Input(stops), Rules);

        eta[1].Eta.ShouldBe(arrived.AddMinutes(30).AddHours(1)); // leaves when its 30 minutes are up, then an hour of road
    }

    [Theory]
    [InlineData(5, RiskStatus.OnTime)]
    [InlineData(10, RiskStatus.OnTime)]
    [InlineData(18, RiskStatus.AtRisk)]
    [InlineData(24, RiskStatus.AtRisk)]
    [InlineData(32, RiskStatus.Delayed)]
    [InlineData(60, RiskStatus.Delayed)]
    [InlineData(70, RiskStatus.SeverelyDelayed)]
    public void How_late_the_arrival_will_be_decides_the_risk(int lateMinutes, RiskStatus expected)
    {
        var planned = Now.AddHours(2);
        var stop = Stop(1, 100, planned.AddMinutes(-lateMinutes));

        var eta = EtaCalculator.Calculate(Input([stop]), Rules);

        eta[0].DelayMinutes.ShouldBe(lateMinutes);
        eta[0].Risk.ShouldBe(expected);
    }

    [Fact]
    public void The_delivery_window_not_the_planned_time_is_what_lateness_is_judged_against()
    {
        var stop = Stop(1, 100, Now.AddHours(1), windowEnd: Now.AddHours(3));

        EtaCalculator.Calculate(Input([stop]), Rules)[0].Risk.ShouldBe(RiskStatus.OnTime); // 2 h to arrive, 3 h window
    }

    [Fact]
    public void A_trip_already_past_its_time_is_late_whatever_the_estimate_says()
    {
        var stop = Stop(1, 1, Now.AddMinutes(-45));

        var eta = EtaCalculator.Calculate(Input([stop]), Rules);

        eta[0].DelayMinutes.ShouldBeGreaterThanOrEqualTo(45);
        eta[0].Risk.ShouldBe(RiskStatus.Delayed);
    }

    [Fact]
    public void Without_a_time_to_meet_the_risk_is_unknown_not_on_time()
    {
        EtaCalculator.Calculate(Input([Stop(1, 100, null)]), Rules)[0].Risk.ShouldBe(RiskStatus.Unknown);
    }

    [Fact]
    public void Confidence_falls_with_stale_tracking_a_deviation_and_few_samples_and_never_claims_certainty()
    {
        var stop = Stop(1, 100, Now.AddHours(3));

        var good = EtaCalculator.Calculate(Input([stop]), Rules)[0].Confidence;
        var stale = EtaCalculator.Calculate(Input([stop], health: TrackingHealth.Stale), Rules)[0].Confidence;
        var lost = EtaCalculator.Calculate(Input([stop], health: TrackingHealth.Lost), Rules)[0].Confidence;
        var off = EtaCalculator.Calculate(Input([stop], offRoute: true), Rules)[0].Confidence;
        var fewSamples = EtaCalculator.Calculate(Input([stop], samples: 1), Rules)[0].Confidence;

        good.ShouldBeLessThan(0.96);
        stale.ShouldBeLessThan(good);
        lost.ShouldBeLessThan(stale);
        off.ShouldBeLessThan(good);
        fewSamples.ShouldBeLessThan(good);
        lost.ShouldBeGreaterThanOrEqualTo(0.2);
    }

    [Fact]
    public void Without_a_route_it_falls_back_to_a_straight_line_stretched_for_roads_and_says_so_in_its_confidence()
    {
        var withPoint = new EtaStop(Guid.NewGuid(), 1, StopKind.Drop, "A", new GeoPoint(19.5, 73), null, null, null, 0, StopStatus.Pending, null);
        var input = new EtaInput(Now, Here, null, false, true, null, 0, [withPoint], TrackingHealth.Healthy, false);

        var eta = EtaCalculator.Calculate(input, Rules)[0];

        eta.RemainingKm.ShouldBeGreaterThan(Geo.DistanceKm(Here, new GeoPoint(19.5, 73)));
        eta.Confidence.ShouldBeLessThan(0.7);
    }

    [Fact]
    public void Stops_already_left_are_not_estimated()
    {
        var done = Stop(1, 10, null, status: StopStatus.Departed);

        EtaCalculator.Calculate(Input([done, Stop(2, 60, null)]), Rules).ShouldHaveSingleItem();
    }
}
