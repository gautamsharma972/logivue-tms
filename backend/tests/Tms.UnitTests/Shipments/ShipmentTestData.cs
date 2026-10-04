using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Shipments;

internal static class ShipmentTestData
{
    public static readonly Guid Tenant = Guid.NewGuid();
    public static readonly Guid Transporter = Guid.NewGuid();
    public static readonly DateOnly Today = new(2026, 7, 1);
    public static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    public static Party Plant => new("Acme Plant", "MIDC Phase 2", "Pune", "Maharashtra", "411019", "Ravi", "9876543210");

    public static Party Customer(string name, string city, string state) => new(name, "Main Road", city, state, "400001", null, null);

    public static int OrderCounter;

    public static Order NewOrder(decimal weight = 1000m, decimal? volume = 5m, Party? pickup = null, Party? drop = null, OrderDirection direction = OrderDirection.Forward, DateOnly? ready = null, DateOnly? by = null) =>
        Order.Create(Tenant, $"ORD-{++OrderCounter:D5}", direction, null, pickup ?? Plant, drop ?? Customer("Shah Traders", "Surat", "Gujarat"), weight, volume, 10, "Packed goods", ready ?? Today, by, null).Value;

    public static VehicleTypeInfo Type(string name, int payload, decimal? volume = null, bool active = true) => new(Guid.NewGuid(), name.ToUpperInvariant(), name, payload, volume, active);

    public static readonly VehicleTypeInfo Ace = Type("Ace", 750, 3.5m);
    public static readonly VehicleTypeInfo Truck14 = Type("14ft", 4000, 18m);
    public static readonly VehicleTypeInfo Truck32 = Type("32ft", 16000, 65m);
    public static IReadOnlyList<VehicleTypeInfo> Types => [Truck32, Ace, Truck14]; // deliberately unsorted

    public static FleetVehicle Vehicle(Guid? transporter = null, int payload = 16000, FleetCompliance compliance = FleetCompliance.Compliant, bool active = true, string plate = "MH12AB1234") =>
        new(Guid.NewGuid(), transporter ?? Transporter, plate, Guid.NewGuid(), "32ft", payload, active, compliance, compliance == FleetCompliance.NonCompliant ? ["Insurance expired on 01 Jun 2026"] : []);

    public static FleetDriver Driver(Guid? transporter = null, FleetCompliance compliance = FleetCompliance.Compliant, bool active = true) =>
        new(Guid.NewGuid(), transporter ?? Transporter, "Ramesh Yadav", "9876543210", "MH1220190001234", active, compliance, compliance == FleetCompliance.NonCompliant ? ["Driving licence missing"] : []);

    public static FreightQuoteResult Quote(decimal total, Guid? transporter = null) =>
        new(Guid.NewGuid(), "CN-00001", transporter ?? Transporter, "Shree Roadlines", FreightMode.Ftl, "PUNE → SURAT", null, [new FreightQuoteLine("FREIGHT", "Flat", total)], [], total);

    public static Shipment Draft(params Order[] orders) => Shipment.Create(Tenant, "SH-00001", orders, FreightMode.Ftl, Truck32.Id, Today.AddDays(1), 400m).Value;

    /// <summary>A shipment tendered to <see cref="Transporter"/> at the quoted total.</summary>
    public static Shipment Tendered(params Order[] orders)
    {
        var shipment = Draft(orders);
        shipment.RecalculateLoad(orders);
        shipment.Tender(Transporter, Quote(40000m), 40000m, null, Now).IsSuccess.ShouldBeTrue();
        return shipment;
    }

    public static Shipment Accepted(params Order[] orders)
    {
        var shipment = Tendered(orders);
        shipment.Accept(Vehicle(), Driver(), Now).IsSuccess.ShouldBeTrue();
        return shipment;
    }
}
