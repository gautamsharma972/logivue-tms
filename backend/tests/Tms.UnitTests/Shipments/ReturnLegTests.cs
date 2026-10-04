using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class ReturnLegTests
{
    private static Order Return(decimal kg, string pickupCity = "Nashik", Party? drop = null) =>
        NewOrder(kg, 2m, pickup: Customer("Customer", pickupCity, "Maharashtra"), drop: drop ?? Plant, direction: OrderDirection.Reverse);

    [Fact]
    public void A_reverse_order_delivered_back_to_the_origin_can_ride_along_as_a_return()
    {
        var forward = NewOrder(3_000m);
        var back = Return(1_000m);

        var shipment = Shipment.Create(Tenant, "SH-9", [forward, back], FreightMode.Ftl, Truck32.Id, Today, null).Value;
        shipment.RecalculateLoad([forward, back]);

        shipment.Orders.Single(o => o.OrderId == back.Id).IsReturn.ShouldBeTrue();
        shipment.Orders.Single(o => o.OrderId == forward.Id).IsReturn.ShouldBeFalse();
        shipment.OriginCity.ShouldBe(forward.PickupCity);
        shipment.TotalWeightKg.ShouldBe(3_000m); // what leaves the origin; the return is not added to it
    }

    [Fact]
    public void A_reverse_order_going_somewhere_else_cannot_share_a_forward_shipment()
    {
        var result = Shipment.Create(Tenant, "SH-9", [NewOrder(), Return(500m, drop: Customer("Elsewhere", "Nagpur", "Maharashtra"))], FreightMode.Ftl, Truck32.Id, Today, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("shipments.pickup_mismatch");
    }

    [Fact]
    public void The_peak_load_follows_the_run_so_a_return_picked_up_before_the_last_drop_counts_on_top()
    {
        var a = NewOrder(3_000m);
        var b = NewOrder(2_000m);
        var back = Return(2_500m);
        var shipment = Shipment.Create(Tenant, "SH-9", [a, back, b], FreightMode.Ftl, Truck32.Id, Today, null).Value; // deliver a, collect return, deliver b
        shipment.RecalculateLoad([a, b, back]);

        // leaves with 5,000; after a: 2,000; after pickup: 4,500; after b: 2,500
        shipment.TotalWeightKg.ShouldBe(5_000m);
        shipment.PeakOnboardKg.ShouldBe(5_000m);

        var (c, d, heavyReturn) = (NewOrder(3_000m), NewOrder(2_000m), Return(4_000m));
        var s2 = Shipment.Create(Tenant, "SH-10", [c, heavyReturn, d], FreightMode.Ftl, Truck32.Id, Today, null).Value;
        s2.RecalculateLoad([c, d, heavyReturn]); // 5,000 → 2,000 → 6,000 → 4,000
        s2.PeakOnboardKg.ShouldBe(6_000m);
    }

    [Fact]
    public void Accepting_checks_the_peak_not_only_the_outbound_load()
    {
        var a = NewOrder(3_000m);
        var back = Return(3_500m);
        var shipment = Shipment.Create(Tenant, "SH-9", [back, a], FreightMode.Ftl, Truck32.Id, Today, null).Value; // collect the return first, then deliver
        shipment.RecalculateLoad([a, back]);
        shipment.Tender(Transporter, Quote(30_000m), 30_000m, null, Now);

        var small = Vehicle(payload: 6_000); // peak is 6,500
        var result = shipment.Accept(small, Driver(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("shipments.overload");
        shipment.Accept(Vehicle(payload: 7_000), Driver(), Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_reverse_only_shipment_still_works_as_before()
    {
        var one = NewOrder(1_000m, direction: OrderDirection.Reverse, pickup: Customer("Retailer", "Surat", "Gujarat"), drop: Plant);
        var two = NewOrder(1_000m, direction: OrderDirection.Reverse, pickup: Customer("Retailer", "Surat", "Gujarat"), drop: Plant);

        var shipment = Shipment.Create(Tenant, "SH-9", [one, two], FreightMode.Ftl, Truck32.Id, Today, null).Value;
        shipment.RecalculateLoad([one, two]);

        shipment.Orders.ShouldAllBe(o => !o.IsReturn);
        shipment.TotalWeightKg.ShouldBe(2_000m);
    }

    [Fact]
    public void Setting_a_delivery_window_needs_both_times_in_order_and_an_open_order()
    {
        var order = NewOrder();

        order.SetDeliveryWindow(new TimeOnly(9, 0), null).Error.Code.ShouldBe("orders.window_invalid");
        order.SetDeliveryWindow(new TimeOnly(17, 0), new TimeOnly(9, 0)).Error.Code.ShouldBe("orders.window_invalid");
        order.SetDeliveryWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)).IsSuccess.ShouldBeTrue();
        order.DeliveryWindowFrom.ShouldBe(new TimeOnly(9, 0));
        order.SetDeliveryWindow(null, null).IsSuccess.ShouldBeTrue();
        order.DeliveryWindowTo.ShouldBeNull();
    }
}
