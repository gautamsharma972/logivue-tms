using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class CollectionRunTests
{
    private static Party Supplier(string city) => Customer($"{city} Supplier", city, "Maharashtra");

    private static Order Inbound(string supplierCity, decimal kg = 1_000m, Party? drop = null) =>
        Order.Create(Tenant, $"ORD-{++OrderCounter:D5}", OrderDirection.Reverse, null, Supplier(supplierCity), drop ?? Plant, kg, 4m, 5, "Parts", Today, null, null).Value;

    private static Order Returnable(Order o)
    {
        o.SetPlanningAttributes(OrderPriority.Normal, null, HandlingType.Standard, false, true, null, ReturnType.SupplierReturn, "Supplier collection", null, null).IsSuccess.ShouldBeTrue();
        return o;
    }

    [Fact]
    public void Orders_collected_at_several_places_for_one_depot_make_one_collection_shipment_priced_from_the_farthest_pickup()
    {
        var a = Inbound("Chakan");
        var b = Inbound("Talegaon");

        var result = Shipment.CreateCollection(Tenant, "SH-00010", [a, b], b.PickupState, b.PickupCity, FreightMode.Ftl, Truck14.Id, Today, 90m);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);
        var shipment = result.Value;
        shipment.IsCollectionRun.ShouldBeTrue();
        shipment.OriginCity.ShouldBe(b.PickupCity);
        shipment.CollectionCity.ShouldBe(a.DropCity);
        shipment.Orders.Count.ShouldBe(2);
        shipment.Orders.ShouldAllBe(o => !o.IsReturn);
        a.Status.ShouldBe(OrderStatus.Planned);
    }

    [Fact]
    public void A_collection_run_refuses_forward_orders_and_orders_for_another_depot()
    {
        var a = Inbound("Chakan");
        var forward = NewOrder(pickup: Plant, drop: Customer("Shah", "Surat", "Gujarat"));
        var elsewhere = Inbound("Chakan", drop: Customer("Other DC", "Nashik", "Maharashtra"));

        Shipment.CreateCollection(Tenant, "SH-00011", [a, forward], a.PickupState, a.PickupCity, FreightMode.Ftl, Truck14.Id, Today, 50m).Error.Code.ShouldBe("shipments.collection_mismatch");
        Shipment.CreateCollection(Tenant, "SH-00012", [a, elsewhere], a.PickupState, a.PickupCity, FreightMode.Ftl, Truck14.Id, Today, 50m).Error.Code.ShouldBe("shipments.collection_mismatch");
        Shipment.CreateCollection(Tenant, "SH-00013", [Inbound("Chakan")], "Maharashtra", "Nowhere", FreightMode.Ftl, Truck14.Id, Today, 50m).Error.Code.ShouldBe("shipments.collection_mismatch");
    }

    [Fact]
    public void Another_pickup_for_the_same_depot_can_join_later_but_a_stray_order_cannot()
    {
        var first = Inbound("Chakan");
        var shipment = Shipment.CreateCollection(Tenant, "SH-00014", [first], first.PickupState, first.PickupCity, FreightMode.Ftl, Truck14.Id, Today, 50m).Value;

        shipment.AddOrders([Inbound("Talegaon")]).IsSuccess.ShouldBeTrue();
        shipment.Orders.Count.ShouldBe(2);
        shipment.AddOrders([NewOrder()]).Error.Code.ShouldBe("shipments.collection_mismatch");
    }

    [Fact]
    public void Reverse_orders_need_a_type_and_reason_before_they_can_be_planned()
    {
        var order = Inbound("Chakan");

        order.SetPlanningAttributes(OrderPriority.Normal, null, HandlingType.Standard, false, true, null, null, null, null, null).Error.ValidationErrors!.ShouldContainKey("returnType");
        Returnable(order).ReturnType.ShouldBe(ReturnType.SupplierReturn);
    }
}
