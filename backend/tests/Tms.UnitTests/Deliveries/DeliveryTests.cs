using Tms.Modules.Deliveries.Domain;
using static Tms.UnitTests.Deliveries.DeliveryTestData;

namespace Tms.UnitTests.Deliveries;

public class DeliveryTests
{
    [Fact]
    public void A_delivery_without_a_transporter_is_planned_and_with_one_it_is_assigned()
    {
        var planned = Delivery.Create(Tenant, "DLV-1", Header() with { TransporterId = null }, [new("SKU", "x", 1, 1, "PKG")], Driver, Now).Value;
        planned.Status.ShouldBe(DeliveryStatus.Planned);
        New().Status.ShouldBe(DeliveryStatus.Assigned);
    }

    [Fact]
    public void A_delivery_needs_a_customer_and_items_with_quantities()
    {
        Delivery.Create(Tenant, "D", Header() with { CustomerName = " " }, [new("SKU", "x", 1, 1, "PKG")], Driver, Now).Error.ValidationErrors!.ShouldContainKey("customerName");
        Delivery.Create(Tenant, "D", Header(), [], Driver, Now).Error.ValidationErrors!.ShouldContainKey("items");
        Delivery.Create(Tenant, "D", Header(), [new("", "x", 1, 1, "PKG")], Driver, Now).Error.ValidationErrors!.ShouldContainKey("items");
        Delivery.Create(Tenant, "D", Header() with { GeofenceRadiusM = 200 }, [new("S", "x", 1, 1, "PKG")], Driver, Now).Error.ValidationErrors!.ShouldContainKey("geofenceRadiusM"); // no coordinates
    }

    [Fact]
    public void The_driver_moves_through_start_arrive_and_complete_in_order_and_nothing_can_be_skipped()
    {
        var d = New();
        d.Arrive(Here, Driver, Now).Error.Code.ShouldBe("deliveries.invalid_state");
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).Error.Code.ShouldBe("deliveries.invalid_state");

        d.Start(Here, Driver, Now).IsSuccess.ShouldBeTrue();
        d.Start(Here, Driver, Now).Error.Code.ShouldBe("deliveries.invalid_state");
        d.Arrive(Here, Driver, Now.AddMinutes(20)).IsSuccess.ShouldBeTrue();
        d.ActualArrivalAt.ShouldBe(Now.AddMinutes(20));
        d.ArrivalLatitude.ShouldBe(Here.Latitude);

        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).IsSuccess.ShouldBeTrue();
        d.Status.ShouldBe(DeliveryStatus.Delivered);
        d.Events.Select(e => e.EventType).ShouldBe([DeliveryEventType.Created, DeliveryEventType.Assigned, DeliveryEventType.Started, DeliveryEventType.Arrived, DeliveryEventType.Delivered]);
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).Error.Code.ShouldBe("deliveries.invalid_state"); // cannot be completed twice
    }

    [Fact]
    public void A_full_delivery_records_what_was_delivered_and_nothing_short()
    {
        var d = Arrived();
        var done = DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).Value;

        done.HasDiscrepancy.ShouldBeFalse();
        done.Reconciliation.Single().Reconciled.ShouldBeTrue();
        d.Items[0].DeliveredQuantity.ShouldBe(100);
        d.Discrepancies.ShouldBeEmpty();
        d.Outcome.ShouldBe(DeliveryOutcome.Full);
    }

    [Fact]
    public void Ninety_five_delivered_three_short_two_damaged_is_a_partial_delivery_with_two_discrepancies()
    {
        var d = Arrived();
        var done = DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, Qty(d, 95, @short: 3) with { DamagedQuantity = 2, DamageType = "BROKEN", DamageReason = "Crushed" }).Value;

        d.Status.ShouldBe(DeliveryStatus.PartiallyDelivered);
        done.HasDiscrepancy.ShouldBeTrue();
        done.HasMismatch.ShouldBeFalse();
        d.Discrepancies.Select(x => (x.Type, x.Quantity)).ShouldBe([(DiscrepancyType.Shortage, 3m), (DiscrepancyType.Damage, 2m)], ignoreOrder: true);
    }

    [Fact]
    public void Quantities_that_do_not_add_up_are_reported_exactly_never_corrected()
    {
        var d = Arrived();
        var done = DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, Qty(d, 90, @short: 3)).Value; // 7 unaccounted for

        done.HasMismatch.ShouldBeTrue();
        done.Reconciliation.Single().Unaccounted.ShouldBe(7);
        done.Reconciliation.Single().Problems.Single().ShouldContain("7 of 100");
        d.Items[0].DeliveredQuantity.ShouldBe(90);
        d.HasQuantityMismatch.ShouldBeTrue();
    }

    [Fact]
    public void A_tenant_can_refuse_to_complete_a_delivery_whose_quantities_do_not_add_up()
    {
        var d = Arrived();
        var strict = new QuantityRulesSetting(0, true);

        var result = d.Complete(DeliveryOutcome.Shortage, [Qty(d, 90, @short: 3)], null, Now, null, null, Here, strict, Driver, Now);

        result.Error.Code.ShouldBe("deliveries.quantities_do_not_add_up");
        d.Status.ShouldBe(DeliveryStatus.Arrived);
    }

    [Fact]
    public void Negative_and_over_delivered_quantities_are_rejected_unless_an_allowance_is_configured()
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, Qty(d, -1, @short: 101)).Error.Code.ShouldBe("deliveries.quantity_invalid");
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 105)).Error.Code.ShouldBe("deliveries.quantity_invalid");

        var lenient = new QuantityRulesSetting(10, false);
        d.Complete(DeliveryOutcome.Full, [Qty(d, 105)], null, Now, null, null, Here, lenient, Driver, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_shortage_needs_a_reason_and_damage_needs_a_type_and_a_reason()
    {
        var d = Arrived();
        var noReason = Qty(d, 97, @short: 3) with { ShortageReasonCode = null };
        DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, noReason).Error.Code.ShouldBe("deliveries.shortage_reason_required");

        var noDamageType = Qty(d, 98, damaged: 2) with { DamageType = null };
        DeliveryTestData.Complete(d, DeliveryOutcome.Damaged, noDamageType).Error.Code.ShouldBe("deliveries.damage_reason_required");
        d.Status.ShouldBe(DeliveryStatus.Arrived); // nothing half-applied
        d.Items[0].DeliveredQuantity.ShouldBeNull();
    }

    [Fact]
    public void The_outcome_the_driver_picks_must_match_the_quantities()
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 95, @short: 5)).Error.Code.ShouldBe("deliveries.outcome_mismatch");
        DeliveryTestData.Complete(d, DeliveryOutcome.Damaged, Qty(d, 95, @short: 5)).Error.Code.ShouldBe("deliveries.outcome_mismatch");
        DeliveryTestData.Complete(d, DeliveryOutcome.Refused, Qty(d, 100)).Error.Code.ShouldBe("deliveries.outcome_invalid");
    }

    [Fact]
    public void A_partial_delivery_must_say_what_happens_to_the_goods_not_delivered()
    {
        var d = Arrived();
        d.Complete(DeliveryOutcome.Partial, [Qty(d, 80, rejected: 20)], null, Now, null, null, Here, Strict, Driver, Now).Error.Code.ShouldBe("deliveries.disposition_required");

        d.Complete(DeliveryOutcome.Partial, [Qty(d, 80, rejected: 20)], RemainingDisposition.Reschedule, Now, null, null, Here, Strict, Driver, Now).IsSuccess.ShouldBeTrue();
        d.RemainingDisposition.ShouldBe(RemainingDisposition.Reschedule);
        d.Status.ShouldBe(DeliveryStatus.PartiallyDelivered);
    }

    [Fact]
    public void Every_item_must_be_reported_and_only_items_of_this_delivery()
    {
        var d = Arrived(100, ("SKU-002", 50));
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).Error.Code.ShouldBe("deliveries.items_mismatch");
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100), new ItemQuantities(Guid.NewGuid(), 50, 0, 0, 0)).Error.Code.ShouldBe("deliveries.items_mismatch");
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100), Qty(d, 50, index: 1)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_failed_visit_is_an_attempt_and_the_delivery_can_be_tried_again()
    {
        var d = Arrived();
        d.RecordFailedAttempt(" ", null, null, null, Here, Driver, Now).Error.Code.ShouldBe("deliveries.reason_required");
        d.RecordFailedAttempt("CUSTOMER_UNAVAILABLE", "Shop shut", "Back after lunch", null, Here, Driver, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        d.Status.ShouldBe(DeliveryStatus.Attempted);

        d.Arrive(Here, Driver, Now.AddHours(3)).IsSuccess.ShouldBeTrue();
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).IsSuccess.ShouldBeTrue();

        d.Attempts.Select(a => (a.AttemptNumber, a.Result)).ShouldBe([(1, AttemptResult.Failed), (2, AttemptResult.Delivered)]);
        d.Attempts[0].ReasonCode.ShouldBe("CUSTOMER_UNAVAILABLE");
    }

    [Fact]
    public void A_delivery_can_be_failed_with_a_reason_and_rescheduled_keeping_its_history()
    {
        var d = Arrived();
        d.Fail("", null, Here, Driver, Now).Error.Code.ShouldBe("deliveries.reason_required");
        d.Fail("ADDRESS_INCORRECT", "No such street", Here, Driver, Now).IsSuccess.ShouldBeTrue();
        d.Status.ShouldBe(DeliveryStatus.Failed);
        d.Outcome.ShouldBe(DeliveryOutcome.Failed);

        d.Reschedule(Now.AddDays(1), null, null, Driver, Now).IsSuccess.ShouldBeTrue();
        d.Status.ShouldBe(DeliveryStatus.Assigned);
        d.Attempts.Count.ShouldBe(1);
        d.Outcome.ShouldBeNull();
    }

    [Fact]
    public void A_refusal_records_every_item_as_rejected_and_needs_a_reason()
    {
        var d = Arrived();
        d.Refuse("", null, null, Here, Driver, Now).Error.Code.ShouldBe("deliveries.reason_required");
        d.Refuse("WRONG_ITEM", "Anil", "Not what we ordered", Here, Driver, Now).IsSuccess.ShouldBeTrue();

        d.Status.ShouldBe(DeliveryStatus.Refused);
        d.Items[0].RejectedQuantity.ShouldBe(100);
        d.Items[0].DeliveredQuantity.ShouldBe(0);
        d.Discrepancies.Single().Type.ShouldBe(DiscrepancyType.Rejection);
    }

    [Fact]
    public void A_delivery_that_has_reached_the_customer_cannot_be_cancelled_and_a_closed_one_needs_a_finished_state()
    {
        var d = Arrived();
        d.Cancel("changed", Driver, Now).Error.Code.ShouldBe("deliveries.not_cancellable");
        d.Close(null, Driver, Now).Error.Code.ShouldBe("deliveries.not_closable");

        var fresh = New();
        fresh.Cancel(" ", Driver, Now).Error.Code.ShouldBe("deliveries.reason_required");
        fresh.Cancel("Customer cancelled", Driver, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_failed_delivery_can_only_be_closed_with_a_reason()
    {
        var d = Arrived();
        d.Fail("TRAFFIC", null, Here, Driver, Now);
        d.Close(null, Driver, Now).Error.Code.ShouldBe("deliveries.reason_required");
        d.Close("Customer no longer needs it", Driver, Now).IsSuccess.ShouldBeTrue();
        d.Status.ShouldBe(DeliveryStatus.Closed);
    }

    [Fact]
    public void The_one_time_code_is_kept_only_as_a_hash_and_locks_after_too_many_wrong_tries()
    {
        var d = Arrived();
        d.IssueOtp("482913", 30, Driver, Now);
        d.OtpHash.ShouldNotBeNull();
        d.OtpHash.ShouldNotContain("482913");

        d.VerifyOtp("000000", 3, Driver, Now.AddMinutes(1)).Error.Code.ShouldBe("deliveries.otp_wrong");
        d.VerifyOtp("111111", 3, Driver, Now.AddMinutes(1)).Error.Code.ShouldBe("deliveries.otp_wrong");
        d.VerifyOtp("222222", 3, Driver, Now.AddMinutes(1)).Error.Code.ShouldBe("deliveries.otp_wrong");
        d.VerifyOtp("482913", 3, Driver, Now.AddMinutes(1)).Error.Code.ShouldBe("deliveries.otp_locked");
        d.OtpAttempts.ShouldBe(3);
        d.OtpVerified.ShouldBeFalse();

        d.IssueOtp("135790", 30, Driver, Now.AddMinutes(2)); // a fresh code resets the count
        d.VerifyOtp("135790", 3, Driver, Now.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        d.OtpVerified.ShouldBeTrue();
        d.VerifyOtp("anything", 3, Driver, Now.AddMinutes(4)).IsSuccess.ShouldBeTrue(); // already verified
    }

    [Fact]
    public void An_expired_code_is_refused()
    {
        var d = Arrived();
        d.IssueOtp("482913", 30, Driver, Now);
        d.VerifyOtp("482913", 5, Driver, Now.AddMinutes(31)).Error.Code.ShouldBe("deliveries.otp_expired");
    }

    [Fact]
    public void A_delivery_that_has_not_started_can_be_changed_but_one_under_way_cannot()
    {
        var d = New();
        d.Update(Header() with { CustomerName = "New Customer" }, Driver, Now).IsSuccess.ShouldBeTrue();
        d.CustomerName.ShouldBe("New Customer");
        d.Start(Here, Driver, Now);
        d.Update(Header(), Driver, Now).Error.Code.ShouldBe("deliveries.locked");
    }

    [Fact]
    public void Quantity_reconciliation_follows_the_configured_allowance()
    {
        var d = New();
        var item = d.Items[0];
        QuantityReconciliation.Check(item, new ItemQuantities(item.Id, 100, 0, 0, 0), Strict).Reconciled.ShouldBeTrue();
        QuantityReconciliation.Check(item, new ItemQuantities(item.Id, 104, 0, 0, 0), Strict).Reconciled.ShouldBeFalse();
        QuantityReconciliation.Check(item, new ItemQuantities(item.Id, 104, 0, 0, 0), new QuantityRulesSetting(5, false)).Reconciled.ShouldBeTrue();
    }
}
