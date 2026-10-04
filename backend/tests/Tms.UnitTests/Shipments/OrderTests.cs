using Tms.Modules.Shipments.Domain;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class OrderTests
{
    [Fact]
    public void A_new_order_is_open_with_normalised_places_for_grouping()
    {
        var order = NewOrder(drop: Customer("Shah", "  surat ", "gujarat"));

        order.Status.ShouldBe(OrderStatus.Open);
        order.PickupCity.ShouldBe("PUNE");
        order.DropCity.ShouldBe("SURAT");
        order.DropState.ShouldBe("GUJARAT");
        order.Drop.City.ShouldBe("surat", "the display text keeps what was typed");
    }

    [Fact]
    public void Every_problem_is_reported_by_field_at_once()
    {
        var bad = Order.Create(Tenant, "ORD-1", OrderDirection.Forward, new string('x', 70), Plant with { Pincode = "12" }, Customer(" ", "Surat", "Gujarat"),
            0m, -1m, -2, " ", Today, Today.AddDays(-1), null);

        bad.IsFailure.ShouldBeTrue();
        bad.Error.ValidationErrors!.Keys.ShouldBe(
            ["pickup.pincode", "drop.name", "weightKg", "volumeCbm", "packages", "description", "reference", "deliverByDate"], ignoreOrder: true);
    }

    [Fact]
    public void Only_an_open_order_can_be_edited_or_cancelled()
    {
        var order = NewOrder();
        order.Plan(Guid.NewGuid());

        order.Update(OrderDirection.Forward, null, Plant, Customer("X", "Surat", "Gujarat"), 5m, null, null, "Goods", Today, null, null).Error.Code.ShouldBe("orders.not_editable");
        order.Cancel("changed mind").Error.Code.ShouldBe("orders.not_cancellable");

        order.Release();
        order.Cancel(" ").Error.Code.ShouldBe("orders.reason_required");
        order.Cancel("Customer cancelled").IsSuccess.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }
}
