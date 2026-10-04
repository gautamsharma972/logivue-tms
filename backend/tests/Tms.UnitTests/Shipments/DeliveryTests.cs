using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class DeliveryTests
{
    private static readonly DateTimeOffset Later = Now.AddHours(30);

    private static DeliveryDetails Details(int? delivered = null, int? damaged = null, string? remarks = null, string receiver = "S. Shah", DateTimeOffset? at = null) =>
        new(at ?? Now.AddHours(20), receiver, delivered, damaged, remarks);

    private static Shipment OnTheRoad(params Order[] orders)
    {
        var shipment = Accepted(orders);
        shipment.Dispatch(orders.Select((_, i) => $"LR-{i + 1:D6}").ToList(), orders, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        shipment.ClearDomainEvents();
        return shipment;
    }

    [Fact]
    public void The_shipment_is_delivered_when_its_last_order_is()
    {
        var a = NewOrder();
        var b = NewOrder();
        var shipment = OnTheRoad(a, b);

        shipment.RecordDelivery(a, Details(), Later).IsSuccess.ShouldBeTrue();
        shipment.Status.ShouldBe(ShipmentStatus.Dispatched);
        shipment.DomainEvents.OfType<ShipmentDelivered>().ShouldBeEmpty();

        shipment.RecordDelivery(b, Details(at: Now.AddHours(25)), Later).IsSuccess.ShouldBeTrue();
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.DeliveredAt.ShouldBe(Now.AddHours(25)); // the last order's time
        shipment.DomainEvents.OfType<ShipmentDelivered>().ShouldHaveSingleItem();
        b.Status.ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public void A_full_delivery_records_who_received_it_and_all_packages()
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);

        shipment.RecordDelivery(order, Details(receiver: "  Store manager "), Later).IsSuccess.ShouldBeTrue();

        var link = shipment.Orders.Single();
        link.ReceiverName.ShouldBe("Store manager");
        (link.PackagesShipped, link.DeliveredPackages, link.DamagedPackages, link.ShortagePackages).ShouldBe((10, 10, 0, 0));
        link.HasException.ShouldBeFalse();
        link.PodStatus.ShouldBe(PodStatus.Awaiting);
        shipment.DomainEvents.OfType<DeliveryExceptionReported>().ShouldBeEmpty();
    }

    [Fact]
    public void A_shortage_needs_an_explanation_and_raises_a_delivery_exception_for_claims()
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);

        var refused = shipment.RecordDelivery(order, Details(delivered: 8), Later);
        refused.Error.ValidationErrors!.ShouldContainKey("remarks");

        shipment.RecordDelivery(order, Details(delivered: 8, remarks: "Two cartons missing at unloading"), Later).IsSuccess.ShouldBeTrue();

        var raised = shipment.DomainEvents.OfType<DeliveryExceptionReported>().ShouldHaveSingleItem();
        (raised.ShortagePackages, raised.DamagedPackages).ShouldBe((2, 0));
        raised.Remarks.ShouldBe("Two cartons missing at unloading");
        shipment.Orders.Single().ShortagePackages.ShouldBe(2);
    }

    [Fact]
    public void Damaged_goods_are_counted_within_what_was_received_and_also_raise_an_exception()
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);

        shipment.RecordDelivery(order, Details(delivered: 10, damaged: 3, remarks: "Water damage"), Later).IsSuccess.ShouldBeTrue();

        var raised = shipment.DomainEvents.OfType<DeliveryExceptionReported>().ShouldHaveSingleItem();
        (raised.ShortagePackages, raised.DamagedPackages).ShouldBe((0, 3));
    }

    [Theory]
    [InlineData(11, 0, "deliveredPackages")]
    [InlineData(-1, 0, "deliveredPackages")]
    [InlineData(5, 6, "damagedPackages")]
    [InlineData(5, -1, "damagedPackages")]
    public void Impossible_quantities_are_refused_with_the_field(int delivered, int damaged, string field)
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);

        var result = shipment.RecordDelivery(order, Details(delivered, damaged, "x"), Later);

        result.IsFailure.ShouldBeTrue();
        result.Error.ValidationErrors!.ShouldContainKey(field);
        order.Status.ShouldBe(OrderStatus.Dispatched);
    }

    [Fact]
    public void An_order_with_no_package_count_cannot_have_quantities_recorded()
    {
        var order = Order.Create(Tenant, "ORD-77777", OrderDirection.Forward, null, Plant, Customer("Shah", "Surat", "Gujarat"), 500m, null, null, "Loose goods", Today, null, null).Value;
        var shipment = OnTheRoad(order);

        shipment.RecordDelivery(order, Details(delivered: 3), Later).IsFailure.ShouldBeTrue();
        shipment.RecordDelivery(order, Details(), Later).IsSuccess.ShouldBeTrue();
        shipment.Orders.Single().DeliveredPackages.ShouldBeNull();
    }

    [Fact]
    public void A_blank_receiver_a_future_time_and_a_time_before_dispatch_are_refused()
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);

        shipment.RecordDelivery(order, Details(receiver: "  "), Later).Error.ValidationErrors!.ShouldContainKey("receiverName");
        shipment.RecordDelivery(order, Details(at: Later.AddHours(2)), Later).Error.ValidationErrors!.ShouldContainKey("deliveredAt");
        shipment.RecordDelivery(order, Details(at: Now.AddMinutes(10)), Later).Error.ValidationErrors!.ShouldContainKey("deliveredAt"); // left at Now + 1h
    }

    [Fact]
    public void A_delivery_can_only_be_recorded_once_and_only_while_on_the_road()
    {
        var order = NewOrder();
        var notLeft = Accepted(order);
        notLeft.RecordDelivery(order, Details(), Later).Error.Code.ShouldBe("shipments.not_dispatched");

        var other = NewOrder();
        var shipment = OnTheRoad(other, NewOrder());
        shipment.RecordDelivery(other, Details(), Later).IsSuccess.ShouldBeTrue();
        shipment.RecordDelivery(other, Details(), Later).Error.Code.ShouldBe("delivery.already_recorded");
    }

    [Fact]
    public void Closing_the_run_in_one_step_completes_only_what_is_left_and_keeps_recorded_details()
    {
        var a = NewOrder();
        var b = NewOrder();
        var shipment = OnTheRoad(a, b);
        shipment.RecordDelivery(a, Details(delivered: 9, remarks: "One short", receiver: "Asha"), Later).IsSuccess.ShouldBeTrue();

        shipment.Deliver([a, b], Later).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.Orders.Single(l => l.OrderId == a.Id).ReceiverName.ShouldBe("Asha");
        shipment.Orders.Single(l => l.OrderId == b.Id).DeliveredAt.ShouldBe(Later);
        shipment.Orders.ShouldAllBe(l => l.IsDelivered);
    }

    // ---- proof of delivery

    private static (Shipment Shipment, Order Order) Delivered()
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);
        shipment.RecordDelivery(order, Details(), Later).IsSuccess.ShouldBeTrue();
        shipment.ClearDomainEvents();
        return (shipment, order);
    }

    [Fact]
    public void Proof_cannot_be_attached_before_the_goods_are_delivered()
    {
        var order = NewOrder();
        var shipment = OnTheRoad(order);

        shipment.ProofAdded(order.Id).Error.Code.ShouldBe("pod.not_delivered");
    }

    [Fact]
    public void Uploaded_proof_is_checked_and_verification_tells_billing()
    {
        var (shipment, order) = Delivered();
        var reviewer = Guid.NewGuid();

        shipment.ProofAdded(order.Id).IsSuccess.ShouldBeTrue();
        shipment.Orders.Single().PodStatus.ShouldBe(PodStatus.Uploaded);
        shipment.VerifyProof(order, reviewer, Later).IsSuccess.ShouldBeTrue();

        var link = shipment.Orders.Single();
        link.PodStatus.ShouldBe(PodStatus.Verified);
        link.PodReviewedBy.ShouldBe(reviewer);
        shipment.DomainEvents.OfType<PodVerified>().ShouldHaveSingleItem().OrderNumber.ShouldBe(order.Number);
    }

    [Fact]
    public void There_is_nothing_to_verify_or_reject_until_proof_is_uploaded()
    {
        var (shipment, order) = Delivered();

        shipment.VerifyProof(order, null, Later).Error.Code.ShouldBe("pod.not_uploaded");
        shipment.RejectProof(order.Id, null, "blurry", Later).Error.Code.ShouldBe("pod.not_uploaded");
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_a_new_upload_clears_it()
    {
        var (shipment, order) = Delivered();
        shipment.ProofAdded(order.Id);

        shipment.RejectProof(order.Id, null, " ", Later).Error.Code.ShouldBe("pod.reason_required");
        shipment.RejectProof(order.Id, null, "Signature is not legible", Later).IsSuccess.ShouldBeTrue();
        shipment.Orders.Single().PodStatus.ShouldBe(PodStatus.Rejected);
        shipment.Orders.Single().PodRejectionReason.ShouldBe("Signature is not legible");

        shipment.ProofAdded(order.Id).IsSuccess.ShouldBeTrue();
        shipment.Orders.Single().PodStatus.ShouldBe(PodStatus.Uploaded);
        shipment.Orders.Single().PodRejectionReason.ShouldBeNull();
    }

    [Fact]
    public void Verified_proof_is_final_and_removing_the_last_file_makes_it_outstanding_again()
    {
        var (shipment, order) = Delivered();
        shipment.ProofAdded(order.Id);
        shipment.ProofRemoved(order.Id, remaining: 1).IsSuccess.ShouldBeTrue();
        shipment.Orders.Single().PodStatus.ShouldBe(PodStatus.Uploaded);
        shipment.ProofRemoved(order.Id, remaining: 0).IsSuccess.ShouldBeTrue();
        shipment.Orders.Single().PodStatus.ShouldBe(PodStatus.Awaiting);

        shipment.ProofAdded(order.Id);
        shipment.VerifyProof(order, null, Later);

        shipment.ProofRemoved(order.Id, 0).Error.Code.ShouldBe("pod.verified");
        shipment.ProofAdded(order.Id).Error.Code.ShouldBe("pod.verified");
    }
}
