using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Deliveries.DeliveryTestData;

namespace Tms.UnitTests.Deliveries;

public class LifecycleEventTests
{
    [Fact]
    public void Every_step_of_a_delivery_is_published_for_other_modules()
    {
        var d = New();
        d.Start(Here, Driver, Now);
        d.Arrive(Here, Driver, Now.AddMinutes(20));
        d.RecordFailedAttempt("CUSTOMER_UNAVAILABLE", "Shop shut", null, null, Here, Driver, Now.AddMinutes(25));
        d.Arrive(Here, Driver, Now.AddMinutes(60));
        Complete(d, DeliveryOutcome.Damaged, Qty(d, 96, 0, 4));

        d.DomainEvents.Select(e => e.GetType()).ShouldBe(
        [
            typeof(DeliveryAssigned), typeof(DeliveryStarted), typeof(VehicleArrived), typeof(DeliveryAttempted), typeof(VehicleArrived),
            typeof(DeliveryCompleted), typeof(DeliveryPartiallyCompleted), typeof(DamageRecorded),
        ], ignoreOrder: true);
        d.DomainEvents.OfType<DamageRecorded>().Single().Quantity.ShouldBe(4);
        d.DomainEvents.OfType<DeliveryCompleted>().Single().DamagedQuantity.ShouldBe(4);
    }

    [Fact]
    public void A_clean_delivery_publishes_no_shortage_or_damage()
    {
        var d = Arrived();
        Complete(d, DeliveryOutcome.Full, Qty(d, 100));

        d.DomainEvents.OfType<ShortageRecorded>().ShouldBeEmpty();
        d.DomainEvents.OfType<DamageRecorded>().ShouldBeEmpty();
        d.DomainEvents.OfType<DeliveryPartiallyCompleted>().ShouldBeEmpty();
    }

    [Fact]
    public void A_shortage_and_a_refusal_are_published()
    {
        var d = Arrived();
        Complete(d, DeliveryOutcome.Shortage, Qty(d, 95, 5));
        d.DomainEvents.OfType<ShortageRecorded>().Single().Quantity.ShouldBe(5);

        var refused = Arrived();
        refused.Refuse("WRONG_ITEM", "Anil", "No", Here, Driver, Now.AddHours(1));
        refused.DomainEvents.OfType<CustomerRefused>().ShouldHaveSingleItem();

        var failed = Arrived();
        failed.Fail("CUSTOMER_UNAVAILABLE", "Shut", Here, Driver, Now.AddHours(1));
        failed.DomainEvents.OfType<DeliveryFailed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_proof_becoming_captured_and_being_checked_and_sent_back_is_published()
    {
        var d = Arrived();
        Complete(d, DeliveryOutcome.Full, Qty(d, 100));
        var pod = PodFor(d);
        AddPhoto(pod);

        pod.Refresh(pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now)).ShouldBeTrue();
        pod.RaiseCaptured(d, Now);
        pod.RaiseValidated(d, Now);
        pod.Submit(pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now), Now).IsSuccess.ShouldBeTrue();
        pod.RequestResubmission("Add a photo of the bay", null, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        pod.RaiseResubmissionRequested(d, "Add a photo of the bay", Now.AddHours(1));

        pod.DomainEvents.Select(e => e.GetType()).ShouldBe([typeof(PodCaptured), typeof(PodValidated), typeof(PodResubmissionRequested)]);
        pod.Refresh(pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now)).ShouldBeFalse(); // not a fresh capture
    }
}
