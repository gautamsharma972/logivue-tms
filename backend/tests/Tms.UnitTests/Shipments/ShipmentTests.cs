using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class ShipmentTests
{
    [Fact]
    public void Creating_a_shipment_plans_its_orders_and_sequences_the_drops()
    {
        var a = NewOrder(drop: Customer("A", "Surat", "Gujarat"));
        var b = NewOrder(drop: Customer("B", "Vapi", "Gujarat"));

        var shipment = Draft(a, b);

        shipment.Status.ShouldBe(ShipmentStatus.Draft);
        shipment.OriginCity.ShouldBe("PUNE");
        shipment.OrdersInDropSequence.Select(o => (o.OrderId, o.DropSequence)).ShouldBe([(a.Id, 1), (b.Id, 2)]);
        new[] { a, b }.ShouldAllBe(o => o.Status == OrderStatus.Planned && o.ShipmentId == shipment.Id);
    }

    [Fact]
    public void Orders_must_be_open_share_a_pickup_and_not_mix_directions()
    {
        var planned = NewOrder(); planned.Plan(Guid.NewGuid());
        Shipment.Create(Tenant, "SH-1", [planned], FreightMode.Ptl, null, Today, null).Error.Code.ShouldBe("shipments.order_not_open");

        var elsewhere = NewOrder(pickup: Plant with { City = "Nashik" });
        Shipment.Create(Tenant, "SH-2", [NewOrder(), elsewhere], FreightMode.Ptl, null, Today, null).Error.Code.ShouldBe("shipments.pickup_mismatch");

        var reverse = NewOrder(direction: OrderDirection.Reverse);
        Shipment.Create(Tenant, "SH-3", [NewOrder(), reverse], FreightMode.Ptl, null, Today, null).Error.Code.ShouldBe("shipments.direction_mismatch");

        Shipment.Create(Tenant, "SH-4", [], FreightMode.Ptl, null, Today, null).Error.Code.ShouldBe("shipments.no_orders");
        Shipment.Create(Tenant, "SH-5", [NewOrder()], FreightMode.Ftl, null, Today, null).Error.ValidationErrors!.ShouldContainKey("vehicleTypeId");
        Shipment.Create(Tenant, "SH-6", [NewOrder()], FreightMode.Ptl, null, Today, -5m).Error.Code.ShouldBe("shipments.distance_invalid");
    }

    [Fact]
    public void Orders_can_be_added_removed_and_resequenced_only_while_draft()
    {
        var a = NewOrder(drop: Customer("A", "Surat", "Gujarat"));
        var b = NewOrder(drop: Customer("B", "Vapi", "Gujarat"));
        var c = NewOrder(drop: Customer("C", "Navsari", "Gujarat"));
        var shipment = Draft(a, b);

        shipment.AddOrders([c]).IsSuccess.ShouldBeTrue();
        shipment.OrdersInDropSequence.Select(o => o.OrderId).ShouldBe([a.Id, b.Id, c.Id]);

        shipment.Sequence([c.Id, a.Id, b.Id]).IsSuccess.ShouldBeTrue();
        shipment.OrdersInDropSequence.Select(o => o.OrderId).ShouldBe([c.Id, a.Id, b.Id]);
        shipment.Sequence([a.Id, b.Id]).Error.Code.ShouldBe("shipments.sequence_invalid");
        shipment.Sequence([a.Id, a.Id, b.Id]).Error.Code.ShouldBe("shipments.sequence_invalid");

        shipment.RemoveOrder(a).IsSuccess.ShouldBeTrue();
        a.Status.ShouldBe(OrderStatus.Open);
        a.ShipmentId.ShouldBeNull();
        shipment.OrdersInDropSequence.Select(o => o.DropSequence).ShouldBe([1, 2], "the run closes up");
        shipment.AddOrders([a]).IsSuccess.ShouldBeTrue("a released order can be planned again");

        shipment.RemoveOrder(NewOrder()).Error.Code.ShouldBe("shipments.order_not_found");
        shipment.AddOrders([b]).Error.Code.ShouldBe("shipments.order_not_open");
    }

    [Fact]
    public void The_last_order_cannot_be_removed()
    {
        var only = NewOrder();
        var shipment = Draft(only);

        shipment.RemoveOrder(only).Error.Code.ShouldBe("shipments.last_order");
    }

    [Fact]
    public void Load_is_recomputed_from_the_orders_and_ignores_missing_volumes()
    {
        var a = NewOrder(weight: 1000m, volume: 4m);
        var b = NewOrder(weight: 500m, volume: null);
        var shipment = Draft(a, b);

        shipment.RecalculateLoad([a, b]);

        shipment.TotalWeightKg.ShouldBe(1500m);
        shipment.TotalVolumeCbm.ShouldBe(4m);

        var none = NewOrder(volume: null);
        var other = Draft(none);
        other.RecalculateLoad([none]);
        other.TotalVolumeCbm.ShouldBeNull();
    }

    [Fact]
    public void Tendering_to_a_dearer_quote_needs_a_reason_and_to_the_cheapest_does_not()
    {
        var order = NewOrder();
        var shipment = Draft(order);

        var dearer = shipment.Tender(Transporter, Quote(45_000m), cheapestTotal: 40_000m, overrideReason: " ", Now);
        dearer.Error.Code.ShouldBe("shipments.override_reason_required");
        shipment.Status.ShouldBe(ShipmentStatus.Draft);

        shipment.Tender(Transporter, Quote(45_000m), 40_000m, "Only carrier with a refrigerated truck", Now).IsSuccess.ShouldBeTrue();
        shipment.OverrideReason.ShouldBe("Only carrier with a refrigerated truck");
        shipment.FreightEstimate.ShouldBe(45_000m);

        shipment.Withdraw().IsSuccess.ShouldBeTrue();
        shipment.Tender(Transporter, Quote(40_000m), 40_000m, "ignored", Now).IsSuccess.ShouldBeTrue();
        shipment.OverrideReason.ShouldBeNull("no reason is recorded when the cheapest was chosen");
        shipment.Status.ShouldBe(ShipmentStatus.Tendered);
    }

    [Fact]
    public void A_tendered_shipment_is_locked_until_withdrawn()
    {
        var order = NewOrder();
        var shipment = Tendered(order);

        shipment.UpdatePlan(FreightMode.Ftl, Truck32.Id, Today, null).Error.Code.ShouldBe("shipments.not_draft");
        shipment.AddOrders([NewOrder()]).Error.Code.ShouldBe("shipments.not_draft");
        shipment.RemoveOrder(order).Error.Code.ShouldBe("shipments.not_draft");
        Draft(NewOrder()).Withdraw().Error.Code.ShouldBe("shipments.not_tendered");
    }

    [Fact]
    public void Accepting_assigns_the_vehicle_and_driver()
    {
        var shipment = Tendered(NewOrder(weight: 5000m));
        var vehicle = Vehicle(payload: 16000, plate: "MH14XY9999");
        var driver = Driver();

        shipment.Accept(vehicle, driver, Now).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Accepted);
        shipment.VehicleRegistration.ShouldBe("MH14XY9999");
        shipment.DriverName.ShouldBe("Ramesh Yadav");
        shipment.DriverPhone.ShouldBe("9876543210");
        shipment.AcceptedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_non_compliant_overloaded_foreign_or_inactive_fleet_is_refused()
    {
        var order = NewOrder(weight: 9000m);
        var shipment = Tendered(order);
        shipment.RecalculateLoad([order]);

        shipment.Accept(Vehicle(compliance: FleetCompliance.NonCompliant), Driver(), Now).Error.Code.ShouldBe("shipments.vehicle_non_compliant");
        shipment.Accept(Vehicle(), Driver(compliance: FleetCompliance.NonCompliant), Now).Error.Code.ShouldBe("shipments.driver_non_compliant");
        shipment.Accept(Vehicle(payload: 4000), Driver(), Now).Error.Code.ShouldBe("shipments.overload");
        shipment.Accept(Vehicle(transporter: Guid.NewGuid()), Driver(), Now).Error.Code.ShouldBe("shipments.fleet_not_theirs");
        shipment.Accept(Vehicle(), Driver(transporter: Guid.NewGuid()), Now).Error.Code.ShouldBe("shipments.fleet_not_theirs");
        shipment.Accept(Vehicle(active: false), Driver(), Now).Error.Code.ShouldBe("shipments.fleet_inactive");
        shipment.Status.ShouldBe(ShipmentStatus.Tendered, "nothing was assigned by the failed attempts");
        shipment.VehicleId.ShouldBeNull();

        shipment.Accept(Vehicle(compliance: FleetCompliance.ExpiringSoon), Driver(compliance: FleetCompliance.ExpiringSoon), Now).IsSuccess.ShouldBeTrue("papers expiring soon are a warning, not a block");
    }

    [Fact]
    public void Rejecting_returns_the_shipment_to_draft_and_remembers_why()
    {
        var shipment = Tendered(NewOrder());

        shipment.Reject(" ").Error.Code.ShouldBe("shipments.reason_required");
        shipment.Reject("No vehicle available that day").IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Draft);
        shipment.TransporterId.ShouldBeNull();
        shipment.FreightEstimate.ShouldBeNull();
        shipment.RejectionCount.ShouldBe(1);
        shipment.LastRejectionReason.ShouldBe("No vehicle available that day");
    }

    [Fact]
    public void Dispatch_issues_a_lorry_receipt_per_order_in_drop_order_and_announces_it()
    {
        var a = NewOrder(drop: Customer("A", "Surat", "Gujarat"));
        var b = NewOrder(drop: Customer("B", "Vapi", "Gujarat"));
        var shipment = Accepted(a, b);
        shipment.Sequence([b.Id, a.Id]).Error.Code.ShouldBe("shipments.not_draft");

        shipment.Dispatch(["LR-00001"], [a, b], Now).Error.Code.ShouldBe("shipments.lr_count");
        shipment.Dispatch(["LR-00001", "LR-00002"], [a, b], Now).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Dispatched);
        shipment.OrdersInDropSequence.Select(o => o.LrNumber).ShouldBe(["LR-00001", "LR-00002"]);
        new[] { a, b }.ShouldAllBe(o => o.Status == OrderStatus.Dispatched);
        shipment.DomainEvents.OfType<ShipmentDispatched>().ShouldHaveSingleItem().TransporterId.ShouldBe(Transporter);
        shipment.Dispatch(["x", "y"], [a, b], Now).Error.Code.ShouldBe("shipments.not_accepted");
    }

    [Fact]
    public void Only_a_shipment_on_the_road_can_be_delivered()
    {
        var order = NewOrder();
        var shipment = Accepted(order);
        shipment.Deliver([order], Now).Error.Code.ShouldBe("shipments.not_dispatched");

        shipment.Dispatch(["LR-00001"], [order], Now);
        shipment.Deliver([order], Now.AddDays(1)).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.DeliveredAt.ShouldBe(Now.AddDays(1));
        order.Status.ShouldBe(OrderStatus.Delivered);
        shipment.DomainEvents.OfType<ShipmentDelivered>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Cancelling_before_departure_frees_the_orders_but_keeps_the_transporter_reference()
    {
        var order = NewOrder();
        var shipment = Tendered(order);

        shipment.Cancel(" ", [order]).Error.Code.ShouldBe("shipments.reason_required");
        shipment.Cancel("Customer postponed", [order]).IsSuccess.ShouldBeTrue();

        shipment.Status.ShouldBe(ShipmentStatus.Cancelled);
        order.Status.ShouldBe(OrderStatus.Open);
        shipment.TransporterId.ShouldBe(Transporter, "so the transporter can see it was cancelled");

        var onRoad = NewOrder();
        var left = Accepted(onRoad);
        left.Dispatch(["LR-1"], [onRoad], Now);
        left.Cancel("too late", [onRoad]).Error.Code.ShouldBe("shipments.not_cancellable");
    }

    [Fact]
    public void Reassigning_before_departure_swaps_the_fleet_under_the_same_rules()
    {
        var order = NewOrder();
        var shipment = Accepted(order);

        shipment.Reassign(Vehicle(plate: "MH12ZZ0001"), Driver()).IsSuccess.ShouldBeTrue();
        shipment.VehicleRegistration.ShouldBe("MH12ZZ0001");
        shipment.Reassign(Vehicle(compliance: FleetCompliance.NonCompliant), Driver()).Error.Code.ShouldBe("shipments.vehicle_non_compliant");
        Tendered(NewOrder()).Reassign(Vehicle(), Driver()).Error.Code.ShouldBe("shipments.not_accepted");
    }

    [Theory]
    [InlineData(8000, 16000, 0.5)]
    [InlineData(16000, 16000, 1.0)]
    [InlineData(1000, 0, null)]
    public void Weight_utilisation_is_load_over_payload(double load, int payload, double? expected) =>
        Shipment.WeightUtilization((decimal)load, payload).ShouldBe(expected is null ? null : (decimal)expected);
}
