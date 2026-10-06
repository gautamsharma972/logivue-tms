using Tms.Modules.Tracking.Domain;

namespace Tms.UnitTests.Tracking;

public class LocationEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly ValidationSetting Rules = new();
    private static readonly GeoPoint Mumbai = new(19.0760, 72.8777);

    private static LocationInput Fix(double lat = 19.0760, double lon = 72.8777, double? accuracy = 12, double? speed = 40, int minutesAgo = 1, bool mock = false) =>
        new(lat, lon, accuracy, speed, 180, Now.AddMinutes(-minutesAgo), mock);

    [Theory]
    [InlineData(91, 72.8)]
    [InlineData(-91, 72.8)]
    [InlineData(19.0, 181)]
    [InlineData(19.0, -181)]
    public void A_position_that_is_not_on_earth_is_refused(double lat, double lon)
    {
        var verdict = LocationValidator.Validate(Fix(lat, lon), null, Now, Rules);

        verdict.Status.ShouldBe(LocationValidation.Rejected);
        verdict.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void The_position_zero_zero_is_a_device_with_no_fix_and_is_refused()
    {
        LocationValidator.Validate(Fix(0, 0), null, Now, Rules).Status.ShouldBe(LocationValidation.Rejected);
    }

    [Fact]
    public void A_fix_stamped_in_the_future_or_far_in_the_past_is_refused()
    {
        LocationValidator.Validate(Fix(minutesAgo: -30), null, Now, Rules).Anomalies.ShouldBe(LocationAnomaly.FutureTimestamp);
        LocationValidator.Validate(Fix(minutesAgo: -3), null, Now, Rules).Status.ShouldBe(LocationValidation.Valid); // within the tolerance
        LocationValidator.Validate(Fix(minutesAgo: 60 * 24 * 8), null, Now, Rules).Anomalies.ShouldBe(LocationAnomaly.OldTimestamp);
        LocationValidator.Validate(Fix(minutesAgo: 60 * 24 * 2), null, Now, Rules).Status.ShouldBe(LocationValidation.Valid); // old but legitimate: a phone that was offline
    }

    [Fact]
    public void Accuracy_that_is_impossible_is_refused_and_merely_poor_accuracy_is_kept_as_suspicious()
    {
        LocationValidator.Validate(Fix(accuracy: -1), null, Now, Rules).Status.ShouldBe(LocationValidation.Rejected);
        LocationValidator.Validate(Fix(accuracy: 5000), null, Now, Rules).Status.ShouldBe(LocationValidation.Rejected);

        var poor = LocationValidator.Validate(Fix(accuracy: 400), null, Now, Rules);

        poor.Status.ShouldBe(LocationValidation.Suspicious);
        poor.Anomalies.ShouldHaveFlag(LocationAnomaly.PoorAccuracy);
    }

    [Fact]
    public void A_reported_speed_no_truck_can_do_is_suspicious()
    {
        var verdict = LocationValidator.Validate(Fix(speed: 400), null, Now, Rules);

        verdict.Status.ShouldBe(LocationValidation.Suspicious);
        verdict.Anomalies.ShouldHaveFlag(LocationAnomaly.ImpossibleSpeed);
    }

    [Fact]
    public void Mumbai_to_Delhi_in_thirty_seconds_is_a_jump_not_a_journey()
    {
        var previous = new PreviousFix(Mumbai, Now.AddSeconds(-30 - 60), 0);
        var delhi = LocationValidator.Validate(Fix(28.6139, 77.2090, minutesAgo: 1), previous, Now, Rules);

        delhi.Status.ShouldBe(LocationValidation.Suspicious);
        delhi.Anomalies.ShouldHaveFlag(LocationAnomaly.LargeJump);
        delhi.Reasons.ShouldContain(r => r.Contains("km/h"));
    }

    [Fact]
    public void A_normal_move_is_accepted()
    {
        var previous = new PreviousFix(Mumbai, Now.AddMinutes(-4), 0);

        var verdict = LocationValidator.Validate(Fix(19.0862, 72.9100, minutesAgo: 1), previous, Now, Rules);

        verdict.Status.ShouldBe(LocationValidation.Valid);
        verdict.Anomalies.ShouldBe(LocationAnomaly.None);
    }

    [Fact]
    public void The_same_position_again_and_again_while_the_device_claims_to_move_is_a_frozen_or_faked_fix()
    {
        var previous = new PreviousFix(Mumbai, Now.AddMinutes(-4), IdenticalCount: 19);

        LocationValidator.Validate(Fix(speed: 60, minutesAgo: 1), previous, Now, Rules).Anomalies.ShouldHaveFlag(LocationAnomaly.RepeatedCoordinates);
        LocationValidator.Validate(Fix(speed: 0, minutesAgo: 1), previous, Now, Rules).Status.ShouldBe(LocationValidation.Valid); // parked: the same position is right
    }

    [Fact]
    public void A_mock_location_flag_is_suspicious_unless_the_tenant_says_otherwise()
    {
        LocationValidator.Validate(Fix(mock: true), null, Now, Rules).Anomalies.ShouldHaveFlag(LocationAnomaly.MockLocation);
        LocationValidator.Validate(Fix(mock: true), null, Now, Rules with { TreatMockLocationAsSuspicious = false }).Status.ShouldBe(LocationValidation.Valid);
    }

    [Fact]
    public void A_device_that_says_it_moves_fast_while_its_position_does_not_change_is_noted_but_not_discarded()
    {
        var previous = new PreviousFix(Mumbai, Now.AddMinutes(-10), 0);

        var verdict = LocationValidator.Validate(Fix(speed: 60, minutesAgo: 1), previous, Now, Rules);

        verdict.Anomalies.ShouldHaveFlag(LocationAnomaly.ReportedSpeedMismatch);
        verdict.Status.ShouldBe(LocationValidation.Valid);
    }
}

public class HealthEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly HealthSetting Rules = new(10, 30);

    [Theory]
    [InlineData(2, TrackingHealth.Healthy)]
    [InlineData(10, TrackingHealth.Healthy)]
    [InlineData(11, TrackingHealth.Stale)]
    [InlineData(30, TrackingHealth.Stale)]
    [InlineData(31, TrackingHealth.Lost)]
    [InlineData(240, TrackingHealth.Lost)]
    public void Health_follows_the_age_of_the_last_gps_fix(int minutes, TrackingHealth expected)
    {
        var (health, age) = HealthEvaluator.Evaluate(TrackingSessionStatus.Active, Now.AddMinutes(-minutes), Now.AddHours(-5), Now, Rules);

        health.ShouldBe(expected);
        age.ShouldBe(minutes);
    }

    [Fact]
    public void A_session_that_has_not_heard_from_the_phone_yet_is_judged_from_when_it_started()
    {
        HealthEvaluator.Evaluate(TrackingSessionStatus.Active, null, Now.AddMinutes(-5), Now, Rules).Health.ShouldBe(TrackingHealth.Healthy);
        HealthEvaluator.Evaluate(TrackingSessionStatus.Active, null, Now.AddMinutes(-45), Now, Rules).Health.ShouldBe(TrackingHealth.Lost);
    }

    [Fact]
    public void Tracking_that_has_not_started_or_has_finished_is_never_stale()
    {
        HealthEvaluator.Evaluate(TrackingSessionStatus.NotStarted, null, Now, Now, Rules).Health.ShouldBe(TrackingHealth.NotStarted);
        HealthEvaluator.Evaluate(TrackingSessionStatus.Completed, Now.AddDays(-3), Now.AddDays(-4), Now, Rules).Health.ShouldBe(TrackingHealth.Completed);
        HealthEvaluator.Evaluate(TrackingSessionStatus.Cancelled, Now.AddDays(-3), Now.AddDays(-4), Now, Rules).Health.ShouldBe(TrackingHealth.Completed);
    }

    [Fact]
    public void A_driver_who_paused_on_purpose_has_not_gone_missing()
    {
        HealthEvaluator.Evaluate(TrackingSessionStatus.Paused, Now.AddHours(-2), Now.AddHours(-5), Now, Rules).Health.ShouldBe(TrackingHealth.Healthy);
    }

    [Fact]
    public void Age_is_measured_from_the_gps_time_not_from_when_the_server_heard()
    {
        // The last fix was captured 35 minutes ago even if it was uploaded a second ago: the vehicle's position is that old.
        HealthEvaluator.Evaluate(TrackingSessionStatus.Active, Now.AddMinutes(-35), Now.AddHours(-3), Now, Rules).Health.ShouldBe(TrackingHealth.Lost);
    }
}

public class GeometryTests
{
    // Mumbai → Pune, roughly along the old highway.
    private static readonly GeoPoint[] Line = [new(19.0760, 72.8777), new(18.9, 73.2), new(18.75, 73.4), new(18.5204, 73.8567)];

    [Fact]
    public void Distance_between_mumbai_and_pune_is_about_120_km_in_a_straight_line()
    {
        Geo.DistanceKm(new GeoPoint(19.0760, 72.8777), new GeoPoint(18.5204, 73.8567)).ShouldBe(120, 5);
    }

    [Fact]
    public void A_point_on_the_route_is_far_along_it_and_close_to_it_and_one_off_to_the_side_is_far_from_it()
    {
        var route = new RouteGeometry(Line);
        var on = route.Match(new GeoPoint(18.9, 73.2));
        var off = route.Match(new GeoPoint(18.9, 73.2 + 0.05)); // about 5 km east of the road

        on.OffRouteKm.ShouldBeLessThan(0.05);
        on.AlongKm.ShouldBeGreaterThan(30);
        off.OffRouteKm.ShouldBeGreaterThan(3);
        route.LengthKm.ShouldBeGreaterThan(110);
    }

    [Fact]
    public void Progress_does_not_jump_back_when_a_road_passes_the_same_place_twice()
    {
        // An out-and-back route: the car is at the far end, then comes back past the middle again.
        var route = new RouteGeometry([new GeoPoint(19, 73), new GeoPoint(19, 73.2), new GeoPoint(19.001, 73.2), new GeoPoint(19.001, 73)]);
        var there = route.Match(new GeoPoint(19, 73.2));
        var back = route.Match(new GeoPoint(19.001, 73.1), notBeforeKm: there.AlongKm);

        back.AlongKm.ShouldBeGreaterThan(there.AlongKm);
    }

    [Fact]
    public void Points_are_placed_inside_and_outside_a_polygon_and_a_circle()
    {
        GeoPoint[] square = [new(19, 73), new(19, 73.1), new(19.1, 73.1), new(19.1, 73)];
        Geo.InPolygon(new GeoPoint(19.05, 73.05), square).ShouldBeTrue();
        Geo.InPolygon(new GeoPoint(19.2, 73.05), square).ShouldBeFalse();

        var circle = new GeofenceShape(new GeoPoint(19, 73), 200);
        circle.Contains(new GeoPoint(19.001, 73)).ShouldBeTrue();
        circle.Contains(new GeoPoint(19.01, 73)).ShouldBeFalse();
        new GeofenceShape(new GeoPoint(19, 73), 200, square).Contains(new GeoPoint(19.05, 73.05)).ShouldBeTrue(); // a polygon wins over the radius
    }

    [Fact]
    public void An_empty_or_one_point_route_is_not_usable()
    {
        new RouteGeometry([]).IsUsable.ShouldBeFalse();
        new RouteGeometry([new GeoPoint(19, 73)]).IsUsable.ShouldBeFalse();
        new RouteGeometry(Line).IsUsable.ShouldBeTrue();
    }
}

internal static class AnomalyAssertions
{
    public static void ShouldHaveFlag(this LocationAnomaly actual, LocationAnomaly expected) => actual.HasFlag(expected).ShouldBeTrue($"{actual} should include {expected}");
}
