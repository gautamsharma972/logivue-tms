using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class PlanningFleetTests
{
    private sealed class Quotes : IFreightQuoteService
    {
        public Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            var total = request.Mode == FreightMode.Ptl ? (request.WeightKg ?? 0m) * 10m : request.VehicleTypeId == Truck32.Id ? 30_000m : request.VehicleTypeId == Truck14.Id ? 12_000m : 4_000m;
            return Task.FromResult(new FreightQuoteSet(
                [new FreightQuoteResult(Guid.NewGuid(), "CN-1", Transporter, "Shree Roadlines", request.Mode ?? FreightMode.Ftl, "lane", null, [new FreightQuoteLine("FREIGHT", "Freight", total)], [], total)], null));
        }
    }

    private sealed class Fleet(IReadOnlyList<FleetVehicle> vehicles, IReadOnlyList<FleetDriver> drivers) : IFleetDirectory, ITransporterDirectory
    {
        public Task<FleetVehicle?> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default) => Task.FromResult(vehicles.FirstOrDefault(v => v.Id == vehicleId));

        public Task<FleetDriver?> GetDriverAsync(Guid driverId, CancellationToken cancellationToken = default) => Task.FromResult(drivers.FirstOrDefault(d => d.Id == driverId));

        public Task<IReadOnlyList<FleetVehicle>> ListVehiclesAsync(Guid transporterId, CancellationToken cancellationToken = default) => Task.FromResult(vehicles);

        public Task<IReadOnlyList<FleetDriver>> ListDriversAsync(Guid transporterId, CancellationToken cancellationToken = default) => Task.FromResult(drivers);

        public Task<bool> ExistsAsync(Guid transporterId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyDictionary<Guid, TransporterInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, TransporterInfo>>(ids.ToDictionary(id => id, id => new TransporterInfo(id, "SHR", "Shree Roadlines", true)));
    }

    private static FleetVehicle Truck(VehicleTypeInfo type, string plate, FleetAvailability availability = FleetAvailability.Available, DateOnly? from = null) =>
        new(Guid.NewGuid(), Transporter, plate, type.Id, type.Name, type.PayloadKg, true, FleetCompliance.Compliant, [], availability, from);

    private static FleetDriver Driver(string name) => new(Guid.NewGuid(), Transporter, name, "9876543210", $"LIC-{name}", true, FleetCompliance.Compliant, []);

    private static PlannableOrder Plannable(Order o) =>
        new(o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, o.DeliverByDate);

    private static PlanningInput Input(IEnumerable<Order> orders) =>
        new(Today, orders.Select(Plannable).ToList(), Types, new PlanOptions(AllowPtl: false, AllowConsolidation: false), []);

    private static RuleBasedPlanningOptimizer Optimizer(Fleet fleet) => new(new Quotes(), null, fleet, fleet);

    private static Order To(string city, decimal kg) => NewOrder(kg, 5m, drop: Customer($"{city} Store", city, "Gujarat"));

    [Fact]
    public async Task When_the_cheapest_type_is_used_up_the_next_trip_takes_the_next_best_free_vehicle_instead_of_going_unplanned()
    {
        var fleet = new Fleet([Truck(Truck14, "MH12AB0001"), Truck(Truck32, "MH12AB0002")], [Driver("A"), Driver("B")]);

        var plan = await Optimizer(fleet).OptimizeAsync(Input([To("Surat", 3_000m), To("Vapi", 3_000m)]), default);

        plan.Unplanned.ShouldBeEmpty();
        plan.Vehicles.Count.ShouldBe(2);
        plan.Vehicles.Select(v => v.VehicleTypeName).Order().ToArray().ShouldBe(["14ft", "32ft"]);
        plan.Vehicles.Select(v => v.AssignedVehicle!.Registration).Distinct().Count().ShouldBe(2);
        plan.Vehicles.Select(v => v.AssignedDriver!.Name).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task When_no_vehicle_is_left_the_order_is_unplanned_with_the_reason()
    {
        var fleet = new Fleet([Truck(Truck14, "MH12AB0001")], [Driver("A"), Driver("B")]);

        var plan = await Optimizer(fleet).OptimizeAsync(Input([To("Surat", 3_000m), To("Vapi", 3_000m)]), default);

        plan.Vehicles.ShouldHaveSingleItem();
        var left = plan.Unplanned.ShouldHaveSingleItem();
        left.Code.ShouldBe(UnplannedCodes.NoAvailableVehicle);
        left.Reason.ShouldContain("already on another trip");
    }

    [Fact]
    public async Task A_vehicle_in_maintenance_is_skipped_and_one_back_in_service_is_used()
    {
        var fleet = new Fleet(
            [Truck(Truck14, "MH12AB0001", FleetAvailability.InMaintenance, Today.AddDays(3)), Truck(Truck14, "MH12AB0009")], [Driver("A")]);

        var plan = await Optimizer(fleet).OptimizeAsync(Input([To("Surat", 3_000m)]), default);

        plan.Vehicles.ShouldHaveSingleItem().AssignedVehicle!.Registration.ShouldBe("MH12AB0009");
    }

    [Fact]
    public async Task A_vehicle_back_in_service_on_the_planning_day_is_usable()
    {
        var fleet = new Fleet([Truck(Truck14, "MH12AB0001", FleetAvailability.InMaintenance, Today)], [Driver("A")]);

        var plan = await Optimizer(fleet).OptimizeAsync(Input([To("Surat", 3_000m)]), default);

        plan.Vehicles.ShouldHaveSingleItem().AssignedVehicle!.Registration.ShouldBe("MH12AB0001");
    }

    [Fact]
    public async Task Without_fleet_data_planning_still_works_and_assigns_nobody()
    {
        var plan = await new RuleBasedPlanningOptimizer(new Quotes()).OptimizeAsync(Input([To("Surat", 3_000m)]), default);

        var vehicle = plan.Vehicles.ShouldHaveSingleItem();
        vehicle.AssignedVehicle.ShouldBeNull();
        vehicle.AssignedDriver.ShouldBeNull();
    }
}
