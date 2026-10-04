using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Transporters;

public class OperationsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Carrier = Guid.NewGuid();
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly DateTimeOffset Required = new(2026, 10, 6, 9, 0, 0, Ist);

    private static ShipmentFact Fact() => new(
        Guid.NewGuid(), "SH-00007", Carrier, FreightMode.Ftl, Guid.NewGuid(), "Maharashtra", "Pune", "Gujarat", "Surat", new DateOnly(2026, 10, 6), null, 30_000m, null, null, null, []);

    private static VehiclePlacement Placement() => VehiclePlacement.Create(Tenant, Fact(), Guid.NewGuid(), "MH12AB1234", Required, Required.AddDays(-1));

    [Fact]
    public void A_placement_starts_with_the_vehicle_named_and_is_pending_until_its_time_has_passed()
    {
        var p = Placement();

        p.Status.ShouldBe(PlacementStatus.VehicleAssigned);
        p.IsOpen.ShouldBeTrue();
        p.Sla(Required.AddMinutes(-30), 15).ShouldBe(PlacementSla.Pending);
        p.Sla(Required.AddMinutes(10), 15).ShouldBe(PlacementSla.Pending); // inside the grace period
        p.Sla(Required.AddMinutes(20), 15).ShouldBe(PlacementSla.Overdue);
    }

    [Fact]
    public void Placing_within_the_grace_period_is_on_time_and_after_it_is_late_with_the_minutes()
    {
        var onTime = Placement();
        onTime.Place(Required.AddMinutes(10)).IsSuccess.ShouldBeTrue();
        onTime.Sla(Required.AddHours(3), 15).ShouldBe(PlacementSla.OnTime);

        var late = Placement();
        late.Place(Required.AddMinutes(95));
        late.Sla(Required.AddHours(3), 15).ShouldBe(PlacementSla.Late);
        late.DelayMinutes.ShouldBe(95);
    }

    [Fact]
    public void The_steps_follow_in_order_and_a_closed_placement_cannot_be_changed()
    {
        var p = Placement();
        p.StartLoading(Required).Error.Code.ShouldBe("placements.illegal_transition"); // not placed yet
        p.Report(Required.AddMinutes(-30)).IsSuccess.ShouldBeTrue();
        p.Report(Required).IsFailure.ShouldBeTrue(); // only once
        p.Place(Required).IsSuccess.ShouldBeTrue();
        p.StartLoading(Required.AddMinutes(10)).IsSuccess.ShouldBeTrue();

        p.Status.ShouldBe(PlacementStatus.LoadingStarted);
        p.Cancel("Too late", Required).IsFailure.ShouldBeTrue();
        p.NoShow("Never came", Required.AddDays(1), 15).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_no_show_needs_a_reason_and_can_only_be_recorded_after_the_grace_period()
    {
        var p = Placement();

        p.NoShow("Never came", Required.AddMinutes(10), 15).Error.Code.ShouldBe("placements.no_show_too_early");
        p.NoShow(" ", Required.AddHours(1), 15).Error.Code.ShouldBe("placements.reason_required");
        p.NoShow("Never came", Required.AddHours(1), 15).IsSuccess.ShouldBeTrue();
        p.Sla(Required.AddHours(2), 15).ShouldBe(PlacementSla.NoShow);
    }

    [Fact]
    public void A_different_vehicle_is_a_replacement_and_a_reported_vehicle_has_to_be_reported_again()
    {
        var p = Placement();
        p.Report(Required.AddMinutes(-20));
        var other = Guid.NewGuid();

        p.Replace(other, "MH14ZZ0001", Required.AddMinutes(-10)).ShouldBeTrue();

        p.ReplacementCount.ShouldBe(1);
        p.Status.ShouldBe(PlacementStatus.VehicleAssigned);
        p.ReportedAt.ShouldBeNull();
        p.Replace(other, "MH14ZZ0001", Required).ShouldBeFalse(); // the same vehicle again is not a replacement
        p.Place(Required);
        p.Replace(Guid.NewGuid(), "MH14ZZ0002", Required).ShouldBeFalse(); // once placed, the vehicle that came is what happened
    }

    [Fact]
    public void Claims_validate_and_a_resolved_claim_is_closed()
    {
        var today = new DateOnly(2026, 10, 6);

        ClaimRecord.Create(Tenant, Carrier, null, null, ClaimType.Damage, today.AddDays(1), 100m, null, today).Error.ValidationErrors!.ShouldContainKey("claimDate");
        ClaimRecord.Create(Tenant, Carrier, null, null, ClaimType.Damage, today, -1m, null, today).Error.ValidationErrors!.ShouldContainKey("claimValue");
        var claim = ClaimRecord.Create(Tenant, Carrier, null, null, ClaimType.Shortage, today, 0m, "Raised from the delivery", today, "exception:x").Value;

        claim.SetValue(2_500m).IsSuccess.ShouldBeTrue();
        claim.Resolve(Required).IsSuccess.ShouldBeTrue();
        claim.Resolve(Required).Error.Code.ShouldBe("claims.already_resolved");
        claim.SetValue(1m).Error.Code.ShouldBe("claims.resolved");
    }

    [Fact]
    public void A_load_cost_is_on_budget_when_invoiced_at_or_below_the_agreed_amount()
    {
        var date = new DateOnly(2026, 10, 6);

        LoadCost.Create(Tenant, Carrier, Guid.NewGuid(), "SH-1", date, 30_000m, 30_000m).Value.OnBudget.ShouldBeTrue();
        LoadCost.Create(Tenant, Carrier, Guid.NewGuid(), "SH-2", date, 30_000m, 31_500m).Value.OnBudget.ShouldBeFalse();
        LoadCost.Create(Tenant, Carrier, Guid.NewGuid(), "SH-3", date, 0m, 10m).Error.ValidationErrors!.ShouldContainKey("agreedAmount");
    }

    [Fact]
    public void Capacity_cannot_have_more_vehicles_available_than_committed()
    {
        var day = CapacityDay.For(Tenant, Carrier, new DateOnly(2026, 10, 6));

        day.Set(10, 11).Error.Code.ShouldBe("capacity.available_exceeds");
        day.Set(10, 8).IsSuccess.ShouldBeTrue();
        (day.VehiclesCommitted, day.VehiclesAvailable).ShouldBe((10, 8));
    }

    [Fact]
    public void An_alert_moves_open_acknowledged_resolved_and_a_resolved_one_stays_resolved()
    {
        var alert = TransporterAlert.Raise(Tenant, "PICKUP_OVERDUE", AlertSeverity.Medium, Carrier, null, "SH-1", "exec:1", "Pickup is overdue");

        alert.Acknowledge(Required).IsSuccess.ShouldBeTrue();
        alert.Acknowledge(Required).Error.Code.ShouldBe("alerts.illegal_transition");
        alert.Resolve(Required, "Truck arrived").IsSuccess.ShouldBeTrue();
        alert.Resolve(Required, null).Error.Code.ShouldBe("alerts.already_resolved");
        alert.Resolution.ShouldBe("Truck arrived");
    }
}
