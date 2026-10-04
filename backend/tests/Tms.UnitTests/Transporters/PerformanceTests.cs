using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Transporters;

public class PerformanceTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Carrier = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 7, 1, 0, 0, 0, Ist);
    private static readonly DateTimeOffset To = new(2026, 8, 1, 0, 0, 0, Ist);
    private static readonly DateTimeOffset AsOf = new(2026, 7, 20, 12, 0, 0, Ist);
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly DateOnly End = new(2026, 7, 31);
    private static readonly PodSummary NoPod = new(0, 0, 0, 0, 0, 0, null);
    private static readonly DelayReason Traffic = new("TRAFFIC", "Traffic", DelayAttribution.NonCarrier);
    private static readonly DelayReason Breakdown = new("VEHICLE_BREAKDOWN", "Breakdown", DelayAttribution.Carrier);

    private static ShipmentFact Fact(Guid? shipmentId = null) => new(
        shipmentId ?? Guid.NewGuid(), "SH-00001", Carrier, FreightMode.Ftl, Guid.NewGuid(), "Maharashtra", "Pune", "Gujarat", "Surat",
        new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12), null, null, null, []);

    private static LoadExecution Execution(DateTimeOffset plannedPickup, DateTimeOffset? departure, DelayReason? reason = null, DateTimeOffset? plannedDelivery = null, DateTimeOffset? delivery = null)
    {
        var e = LoadExecution.Create(Tenant, Fact(), plannedPickup, plannedDelivery);
        if (departure is { } d)
        {
            e.Record(ExecutionEventType.VehicleDeparture, d, reason, 15, null, null, d).IsSuccess.ShouldBeTrue();
        }

        if (delivery is { } x)
        {
            e.Record(ExecutionEventType.DeliveryComplete, x, reason, 15, null, null, x).IsSuccess.ShouldBeTrue();
        }

        return e;
    }

    private static OperationalInputs Inputs(
        IReadOnlyCollection<LoadExecution>? executions = null, IReadOnlyCollection<PlacementInput>? placements = null, IReadOnlyCollection<TenderInvitation>? invitations = null, PodSummary? pod = null) =>
        new(From, To, AsOf, 15, invitations ?? [], placements ?? [], executions ?? [], pod ?? NoPod);

    private static OperationalKpi Kpi(OperationalPeriodResult r, KpiType type) => r.Kpis.Single(k => k.Type == type);

    private static readonly DateTimeOffset Pickup = new(2026, 7, 10, 8, 0, 0, Ist);

    [Fact]
    public void A_departure_within_the_tolerance_is_on_time_and_carries_no_attribution()
    {
        var e = Execution(Pickup, Pickup.AddMinutes(10));

        e.PickupDelayMinutes.ShouldBe(10);
        e.PickupAttribution.ShouldBe(DelayAttribution.None);
    }

    [Fact]
    public void A_late_departure_without_a_reason_is_unattributed_and_with_a_reason_takes_its_attribution()
    {
        Execution(Pickup, Pickup.AddMinutes(60)).PickupAttribution.ShouldBe(DelayAttribution.Unattributed);
        Execution(Pickup, Pickup.AddMinutes(60), Breakdown).PickupAttribution.ShouldBe(DelayAttribution.Carrier);
        Execution(Pickup, Pickup.AddMinutes(60), Traffic).PickupAttribution.ShouldBe(DelayAttribution.NonCarrier);
    }

    [Fact]
    public void Only_carrier_delays_count_against_on_time_pickup_and_the_rest_are_left_out()
    {
        var result = OperationalKpiCalculator.Calculate(Inputs(executions:
        [
            Execution(Pickup, Pickup),
            Execution(Pickup, Pickup.AddMinutes(60), Breakdown),
            Execution(Pickup, Pickup.AddMinutes(60), Traffic),
            Execution(Pickup, Pickup.AddMinutes(60)),
        ]), Start, End);

        var otp = Kpi(result, KpiType.OnTimePickup);
        otp.Numerator.ShouldBe(1);
        otp.Denominator.ShouldBe(2); // the traffic delay and the unattributed one are in neither
        otp.Value.ShouldBe(50m);
        result.Metrics.LatePickupsNonCarrier.ShouldBe(1);
        result.Metrics.LatePickupsUnattributed.ShouldBe(1);
    }

    [Fact]
    public void A_load_with_no_planned_time_is_not_measurable_not_a_failure()
    {
        var result = OperationalKpiCalculator.Calculate(Inputs(executions: [Execution(plannedPickup: Pickup, departure: null)]), Start, End);

        Kpi(result, KpiType.OnTimePickup).Value.ShouldBeNull();
        Kpi(result, KpiType.OnTimeDelivery).Value.ShouldBeNull();
    }

    [Fact]
    public void Tender_acceptance_counts_answered_invitations_only()
    {
        TenderInvitation Invite(InvitationOutcome outcome)
        {
            var i = TenderInvitation.Create(Tenant, null, Guid.NewGuid(), "SH-1", Carrier, new DateTimeOffset(2026, 7, 5, 9, 0, 0, Ist));
            if (outcome != InvitationOutcome.Open)
            {
                i.Respond(outcome, new DateTimeOffset(2026, 7, 5, 10, 0, 0, Ist), "x");
            }

            return i;
        }

        var result = OperationalKpiCalculator.Calculate(
            Inputs(invitations: [Invite(InvitationOutcome.Accepted), Invite(InvitationOutcome.Accepted), Invite(InvitationOutcome.Rejected), Invite(InvitationOutcome.Open)]), Start, End);

        var acceptance = Kpi(result, KpiType.TenderAcceptance);
        acceptance.Numerator.ShouldBe(2);
        acceptance.Denominator.ShouldBe(3);
        acceptance.Value.ShouldBe(66.67m);
    }

    [Fact]
    public void A_response_can_only_be_given_once()
    {
        var i = TenderInvitation.Create(Tenant, null, Guid.NewGuid(), "SH-1", Carrier, Pickup);

        i.Respond(InvitationOutcome.Accepted, Pickup, null).ShouldBeTrue();
        i.Respond(InvitationOutcome.Rejected, Pickup, "changed my mind").ShouldBeFalse();
        i.Outcome.ShouldBe(InvitationOutcome.Accepted);
    }

    [Fact]
    public void Placements_are_measured_only_once_due_and_within_the_grace_period()
    {
        var due = new DateTimeOffset(2026, 7, 10, 9, 0, 0, Ist);
        var result = OperationalKpiCalculator.Calculate(Inputs(placements:
        [
            new PlacementInput(due, due.AddMinutes(10), false, false, 0), // within grace
            new PlacementInput(due, due.AddMinutes(60), false, false, 1), // late, replaced
            new PlacementInput(due, null, true, false, 0), // no-show
            new PlacementInput(due, null, false, true, 0), // cancelled: ignored
            new PlacementInput(AsOf.AddDays(3), null, false, false, 0), // not due yet: ignored
        ]), Start, End);

        var compliance = Kpi(result, KpiType.PlacementCompliance);
        compliance.Numerator.ShouldBe(1);
        compliance.Denominator.ShouldBe(3);
        Kpi(result, KpiType.NoShowRate).Numerator.ShouldBe(1);
        Kpi(result, KpiType.VehicleReplacementRate).Numerator.ShouldBe(1);
    }

    [Fact]
    public void Proof_of_delivery_is_due_when_submitted_or_past_its_sla_and_on_time_when_within_it()
    {
        var delivered = new DateTimeOffset(2026, 7, 5, 10, 0, 0, Ist);
        var pod = OperationalKpiCalculator.SummarisePod(
        [
            (delivered, delivered.AddHours(5), "Verified", 0), // on time, verified
            (delivered, delivered.AddHours(30), "Uploaded", 0), // late
            (delivered, delivered.AddHours(2), "Uploaded", 1), // on time, refused once and replaced
            (delivered, null, "Awaiting", 0), // never sent, past the SLA
            (AsOf.AddHours(-2), null, "Awaiting", 0), // still inside the SLA: not counted
        ], 24, AsOf);

        pod.DeliveriesRequiringPod.ShouldBe(4);
        pod.SubmittedWithinSla.ShouldBe(2);
        pod.Accepted.ShouldBe(1);
        pod.Rejected.ShouldBe(1);
        pod.Pending.ShouldBe(1);
        pod.AverageSubmissionHours.ShouldBe(12.3);
    }

    [Fact]
    public void Events_must_follow_the_sequence_and_each_be_recorded_once()
    {
        var e = LoadExecution.Create(Tenant, Fact(), Pickup, null);
        e.Record(ExecutionEventType.VehicleArrival, Pickup, null, 15, null, null, Pickup).IsSuccess.ShouldBeTrue();

        e.Record(ExecutionEventType.VehicleArrival, Pickup.AddMinutes(5), null, 15, null, null, Pickup).Error.Code.ShouldBe("executions.sequence_invalid");
        e.Record(ExecutionEventType.LoadingStart, Pickup.AddMinutes(-5), null, 15, null, null, Pickup).Error.Code.ShouldBe("executions.time_order");
        e.Status.ShouldBe(ExecutionStatus.AtPickup);
    }

    [Fact]
    public void A_delay_can_be_attributed_afterwards_without_changing_the_minutes()
    {
        var e = Execution(Pickup, Pickup.AddMinutes(90));

        e.Attribute(delivery: false, Traffic).IsSuccess.ShouldBeTrue();

        e.PickupAttribution.ShouldBe(DelayAttribution.NonCarrier);
        e.PickupDelayMinutes.ShouldBe(90);
        Execution(Pickup, Pickup).Attribute(delivery: false, Traffic).Error.Code.ShouldBe("executions.not_late");
    }

    [Fact]
    public void Planned_times_come_from_the_dates_and_the_configured_time_of_day()
    {
        var (pickup, delivery) = ExecutionService.PlannedTimes(Fact(), new PlannedTimesSetting("18:30", "20:00"));

        pickup.ShouldBe(new DateTimeOffset(2026, 7, 10, 18, 30, 0, Ist));
        delivery.ShouldBe(new DateTimeOffset(2026, 7, 12, 20, 0, 0, Ist));
        ExecutionService.PlannedTimes(Fact() with { DeliverBy = null }, new PlannedTimesSetting("18:30", "20:00")).Delivery.ShouldBeNull();
    }

    private static PerformanceKpi Row(KpiType type, decimal numerator, decimal denominator) =>
        PerformanceKpi.Create(Tenant, Carrier, null, null, type, Start, End, numerator, denominator, denominator == 0 ? null : numerator * 100 / denominator, 1);

    [Fact]
    public void A_kpi_below_the_minimum_sample_is_left_out_and_the_other_weights_are_renormalised()
    {
        var weights = new KpiWeightsSetting(0, 50, 0, 0, 50, 0, 0, 0);
        var scoring = (RecommendationScoringSetting)SettingDefaults.For(SettingKeys.RecommendationScoring)!;

        var measured = ScorecardMath.Measure([Row(KpiType.OnTimeDelivery, 18, 20), Row(KpiType.PodCompliance, 3, 4)], weights, 20, scoring);

        measured.Single(m => m.Kpi == KpiType.OnTimeDelivery).Value.ShouldBe(90m);
        measured.Single(m => m.Kpi == KpiType.PodCompliance).Value.ShouldBeNull(); // 4 loads is too few to judge
        ScorecardMath.Overall(measured).ShouldBe(90m); // judged on the one that could be measured
    }

    [Fact]
    public void Claims_score_falls_to_zero_at_the_configured_rate_and_lower_is_better()
    {
        var scoring = new RecommendationScoringSetting(5, 40, 5, 60);

        ScorecardMath.ToScore(KpiType.ClaimsRate, 0m, scoring).ShouldBe(100m);
        ScorecardMath.ToScore(KpiType.ClaimsRate, 2.5m, scoring).ShouldBe(50m);
        ScorecardMath.ToScore(KpiType.ClaimsRate, 9m, scoring).ShouldBe(0m);
        ScorecardMath.LowerIsBetter(KpiType.ClaimsRate).ShouldBeTrue();
        ScorecardMath.LowerIsBetter(KpiType.OnTimeDelivery).ShouldBeFalse();
    }

    [Fact]
    public void Pooling_adds_counts_across_months_rather_than_averaging_percentages()
    {
        var value = ScorecardMath.Pooled([Row(KpiType.OnTimeDelivery, 1, 1), Row(KpiType.OnTimeDelivery, 18, 99)], KpiType.OnTimeDelivery, 20, out var n, out var d);

        value.ShouldBe(19m); // 19 of 100, not the mean of 100% and 18%
        (n, d).ShouldBe((19m, 100m));
    }

    [Fact]
    public void A_lane_covers_a_load_by_state_and_optionally_city_and_mode()
    {
        var lane = TransporterLane.Create(Tenant, Carrier, "maharashtra", "pune", "gujarat", null, FreightMode.Ftl, 600, new DateOnly(2026, 1, 1), null).Value;

        lane.Covers("Maharashtra", "Pune", "Gujarat", "Surat", FreightMode.Ftl).ShouldBeTrue();
        lane.Covers("Maharashtra", "Nashik", "Gujarat", "Surat", FreightMode.Ftl).ShouldBeFalse(); // lane is from Pune
        lane.Covers("Maharashtra", "Pune", "Gujarat", "Surat", FreightMode.Ptl).ShouldBeFalse();
        lane.Covers("Maharashtra", "Pune", "Goa", "Panaji", FreightMode.Ftl).ShouldBeFalse();
        TransporterLane.Create(Tenant, Carrier, "", null, "Gujarat", null, null, null, new DateOnly(2026, 1, 1), null).Error.ValidationErrors!.ShouldContainKey("originState");
    }

    [Fact]
    public void Every_setting_has_a_default_of_the_shape_the_code_reads()
    {
        SettingDefaults.Keys.ShouldNotBeEmpty();
        ((KpiWeightsSetting)SettingDefaults.For(SettingKeys.KpiWeights)!).ShouldNotBeNull();
        var policy = (DelayPolicySetting)SettingDefaults.For(SettingKeys.ExecutionDelayPolicy)!;
        policy.Find("traffic")!.Attribution.ShouldBe(DelayAttribution.NonCarrier);
        policy.Find("nonsense").ShouldBeNull();
    }
}
