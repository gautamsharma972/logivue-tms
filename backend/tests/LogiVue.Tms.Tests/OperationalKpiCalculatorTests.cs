using FluentAssertions;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Execution;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Milestone 6 KPI rules, tested without a database or host.</summary>
public class OperationalKpiCalculatorTests
{
    private static readonly DateTime From = new(2026, 7, 1);
    private static readonly DateTime To = new(2026, 8, 1);
    private static readonly DateTime AsOf = new(2026, 7, 20, 12, 0, 0);
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly DateOnly End = new(2026, 7, 31);

    private static readonly TransporterPodSummaryDto NoPod = new(1, 0, 0, 0, 0, 0, 0, null);

    private static OperationalInputs Inputs(
        IReadOnlyCollection<LoadExecution>? executions = null,
        IReadOnlyCollection<VehiclePlacementRequest>? placements = null,
        IReadOnlyCollection<Tender>? invitations = null,
        TransporterPodSummaryDto? pod = null,
        int grace = 15) =>
        new(From, To, AsOf, ToleranceMinutes: 15, GraceMinutes: grace,
            invitations ?? [], placements ?? [], executions ?? [], pod ?? NoPod);

    private static LoadExecution Pickup(DelayAttribution attribution, int? minutes = 0) => new()
    {
        LoadReference = "LD-1", TransporterId = 1,
        PlannedPickupAt = new DateTime(2026, 7, 10, 8, 0, 0),
        ActualPickupAt = new DateTime(2026, 7, 10, 8, 0, 0).AddMinutes(minutes ?? 0),
        PickupDelayMinutes = minutes, PickupAttribution = attribution
    };

    private static LoadExecution Delivery(DelayAttribution attribution, DateTime? planned = null) => new()
    {
        LoadReference = "LD-2", TransporterId = 1,
        PlannedDeliveryAt = planned ?? new DateTime(2026, 7, 11, 18, 0, 0),
        ActualDeliveryAt = (planned ?? new DateTime(2026, 7, 11, 18, 0, 0)).AddMinutes(attribution == DelayAttribution.None ? 0 : 90),
        DeliveryDelayMinutes = attribution == DelayAttribution.None ? 0 : 90, DeliveryAttribution = attribution
    };

    private static OperationalKpi Kpi(OperationalPeriodResult result, KpiType type) => result.Kpis.Single(k => k.Type == type);

    [Fact]
    public void Non_carrier_and_unattributed_delays_are_left_out_of_on_time_pickup()
    {
        var result = OperationalKpiCalculator.Calculate(Inputs(executions:
        [
            Pickup(DelayAttribution.None),
            Pickup(DelayAttribution.Carrier, 60),
            Pickup(DelayAttribution.NonCarrier, 60),
            Pickup(DelayAttribution.Unattributed, 60)
        ]), Start, End);

        var otp = Kpi(result, KpiType.OnTimePickup);
        otp.Numerator.Should().Be(1);
        otp.Denominator.Should().Be(2, "one on-time pickup and one carrier-attributable delay");
        otp.Value.Should().Be(50m);
        result.Metrics.LatePickupsNonCarrier.Should().Be(1);
        result.Metrics.LatePickupsUnattributed.Should().Be(1);
    }

    [Fact]
    public void A_KPI_with_nothing_to_measure_is_not_measurable_rather_than_zero()
    {
        var result = OperationalKpiCalculator.Calculate(Inputs(), Start, End);

        var otd = Kpi(result, KpiType.OnTimeDelivery);
        otd.Denominator.Should().Be(0);
        otd.Value.Should().BeNull();
        Kpi(result, KpiType.PodCompliance).Value.Should().BeNull();
    }

    [Fact]
    public void A_delivery_without_a_planned_time_is_excluded_and_reported_as_not_measurable()
    {
        var unplanned = new LoadExecution
        {
            LoadReference = "LD-3", TransporterId = 1, PlannedDeliveryAt = null,
            ActualDeliveryAt = new DateTime(2026, 7, 12, 9, 0, 0), DeliveryAttribution = DelayAttribution.None
        };

        var result = OperationalKpiCalculator.Calculate(Inputs(executions: [unplanned]), Start, End);

        Kpi(result, KpiType.OnTimeDelivery).Value.Should().BeNull();
        result.Metrics.NotMeasurableDeliveries.Should().Be(1);
    }

    [Fact]
    public void Placement_compliance_only_measures_placements_that_are_due()
    {
        var placedOnTime = new VehiclePlacementRequest
        {
            LoadReference = "A", TransporterId = 1, RequiredPlacementAt = new DateTime(2026, 7, 10, 6, 0, 0),
            PlacedAt = new DateTime(2026, 7, 10, 6, 10, 0), Status = PlacementStatus.Placed
        };
        var placedLate = new VehiclePlacementRequest
        {
            LoadReference = "B", TransporterId = 1, RequiredPlacementAt = new DateTime(2026, 7, 11, 6, 0, 0),
            PlacedAt = new DateTime(2026, 7, 11, 8, 0, 0), Status = PlacementStatus.Placed
        };
        var notYetDue = new VehiclePlacementRequest
        {
            LoadReference = "C", TransporterId = 1, RequiredPlacementAt = new DateTime(2026, 7, 25, 6, 0, 0),
            Status = PlacementStatus.Requested
        };

        var result = OperationalKpiCalculator.Calculate(Inputs(placements: [placedOnTime, placedLate, notYetDue]), Start, End);

        var compliance = Kpi(result, KpiType.PlacementCompliance);
        compliance.Denominator.Should().Be(2, "the placement due on 25 July is still in flight");
        compliance.Numerator.Should().Be(1);
        result.Metrics.DuePlacements.Should().Be(2);
    }

    [Fact]
    public void A_placement_within_the_grace_period_counts_as_on_time()
    {
        var placement = new VehiclePlacementRequest
        {
            LoadReference = "A", TransporterId = 1, RequiredPlacementAt = new DateTime(2026, 7, 10, 6, 0, 0),
            PlacedAt = new DateTime(2026, 7, 10, 6, 20, 0), Status = PlacementStatus.Placed
        };

        var strict = OperationalKpiCalculator.Calculate(Inputs(placements: [placement], grace: 0), Start, End);
        var graced = OperationalKpiCalculator.Calculate(Inputs(placements: [placement], grace: 30), Start, End);

        Kpi(strict, KpiType.PlacementCompliance).Numerator.Should().Be(0);
        Kpi(graced, KpiType.PlacementCompliance).Numerator.Should().Be(1);
    }

    [Fact]
    public void Open_and_cancelled_invitations_are_excluded_from_tender_acceptance()
    {
        var invitations = new[]
        {
            new Tender { SentAt = new DateTime(2026, 7, 2), Status = TenderStatus.Accepted },
            new Tender { SentAt = new DateTime(2026, 7, 3), Status = TenderStatus.Rejected },
            new Tender { SentAt = new DateTime(2026, 7, 4), Status = TenderStatus.Sent },
            new Tender { SentAt = new DateTime(2026, 7, 5), Status = TenderStatus.Cancelled },
            new Tender { SentAt = null, Status = TenderStatus.Draft }
        };

        var result = OperationalKpiCalculator.Calculate(Inputs(invitations: invitations), Start, End);

        var acceptance = Kpi(result, KpiType.TenderAcceptance);
        acceptance.Numerator.Should().Be(1);
        acceptance.Denominator.Should().Be(2);
        acceptance.Value.Should().Be(50m);
    }

    [Fact]
    public void POD_compliance_and_rejection_use_the_provider_summary()
    {
        var pod = new TransporterPodSummaryDto(1, DeliveriesRequiringPod: 4, SubmittedWithinSla: 3, Accepted: 2, Rejected: 1, Reviewed: 3, Pending: 1, AverageSubmissionHours: 6.5);

        var result = OperationalKpiCalculator.Calculate(Inputs(pod: pod), Start, End);

        Kpi(result, KpiType.PodCompliance).Value.Should().Be(75m);
        Kpi(result, KpiType.PodRejectionRate).Value.Should().Be(33.33m);
        result.Metrics.PendingPod.Should().Be(1);
        result.Metrics.AveragePodSubmissionHours.Should().Be(6.5);
    }

    [Fact]
    public void KPI_percentages_are_derived_from_stored_numerator_and_denominator()
    {
        var kpi = new OperationalKpi(KpiType.OnTimeDelivery, 942, 1000);

        kpi.Value.Should().Be(94.2m);
    }

    [Fact]
    public void Placement_sla_status_reflects_the_grace_period_and_terminal_states()
    {
        var required = new DateTime(2026, 7, 10, 6, 0, 0);
        var placed = new VehiclePlacementRequest { RequiredPlacementAt = required, PlacedAt = required.AddMinutes(10), Status = PlacementStatus.Placed };
        var lateOpen = new VehiclePlacementRequest { RequiredPlacementAt = required, Status = PlacementStatus.Confirmed };
        var pending = new VehiclePlacementRequest { RequiredPlacementAt = required, Status = PlacementStatus.Confirmed };
        var noShow = new VehiclePlacementRequest { RequiredPlacementAt = required, Status = PlacementStatus.NoShow };

        PlacementRules.SlaStatus(placed, required.AddDays(1), 15).Should().Be("OnTime");
        PlacementRules.SlaStatus(lateOpen, required.AddHours(1), 15).Should().Be("Overdue");
        PlacementRules.SlaStatus(pending, required.AddMinutes(-30), 15).Should().Be("Pending");
        PlacementRules.SlaStatus(noShow, required.AddDays(1), 15).Should().Be("NoShow");
        PlacementRules.DelayMinutes(placed).Should().Be(10);
        PlacementRules.DelayMinutes(pending).Should().BeNull();
    }

    [Fact]
    public void Event_within_tolerance_is_on_time()
    {
        var planned = new DateTime(2026, 7, 10, 8, 0, 0);

        var (minutes, code, attribution) = ExecutionService.Evaluate(planned, planned.AddMinutes(10), Policy(), null, null);

        minutes.Should().Be(10);
        code.Should().BeNull();
        attribution.Should().Be(DelayAttribution.None);
    }

    [Fact]
    public void Late_event_without_a_reason_needs_attribution()
    {
        var planned = new DateTime(2026, 7, 10, 8, 0, 0);

        var (_, code, attribution) = ExecutionService.Evaluate(planned, planned.AddMinutes(40), Policy(), null, null);

        code.Should().BeNull();
        attribution.Should().Be(DelayAttribution.Unattributed);
    }

    [Fact]
    public void Late_event_takes_the_attribution_of_its_reason()
    {
        var planned = new DateTime(2026, 7, 10, 8, 0, 0);
        var policy = Policy();

        var (minutes, code, attribution) = ExecutionService.Evaluate(planned, planned.AddMinutes(40), policy, policy.Reasons[0], "TRAFFIC");

        minutes.Should().Be(40);
        code.Should().Be("TRAFFIC");
        attribution.Should().Be(DelayAttribution.NonCarrier);
    }

    [Fact]
    public void Event_without_a_planned_time_is_not_measurable()
    {
        var (minutes, _, attribution) = ExecutionService.Evaluate(null, new DateTime(2026, 7, 10, 8, 0, 0), Policy(), null, null);

        minutes.Should().BeNull();
        attribution.Should().Be(DelayAttribution.None);
    }

    private static DelayPolicySetting Policy() => new(15, [new DelayReasonSetting("TRAFFIC", "Traffic", "NonCarrier")]);
}
