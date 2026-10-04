using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Shipments;

public class PlanningKpiTests
{
    private static PlannedVehicle Vehicle(
        FreightMode mode = FreightMode.Ftl, decimal cost = 10_000m, decimal weightKg = 4_000m, decimal? weightUtil = 0.5m, decimal? volumeUtil = 0.4m, double? km = 100,
        int deliveries = 1, int returns = 0, decimal? consolidation = null, decimal? backhaul = null, string transporter = "Shree", string type = "14ft")
    {
        var orders = Enumerable.Range(0, deliveries).Select(i => new PlannedOrder(Guid.NewGuid(), $"D{i}", i + 1, "X", "Y", weightKg / deliveries, null))
            .Concat(Enumerable.Range(0, returns).Select(i => new PlannedOrder(Guid.NewGuid(), $"R{i}", deliveries + i + 1, "X", "Y", 500m, null, "ReturnPickup"))).ToList();
        return new PlannedVehicle(
            Guid.NewGuid(), "Pune", "MH", mode, Guid.NewGuid(), type, 8000, 30m, null, null, null, transporter, cost, [], weightKg, null, weightUtil, volumeUtil, orders, [], "reason", false,
            consolidation, DistanceKm: km, BackhaulSaving: backhaul);
    }

    private static KpiPlan Plan(DateOnly date, PlanStatus status, IReadOnlyList<PlannedVehicle> vehicles, IReadOnlyList<UnplannedOrder>? unplanned = null) =>
        new(date, status, PlanSnapshot.Build(SolverStatus.Feasible, null, vehicles, unplanned ?? []));

    private static readonly DateOnly D1 = new(2026, 7, 1);

    [Fact]
    public void An_empty_period_gives_zeros_and_no_averages_rather_than_errors()
    {
        var k = PlanningKpiCalculator.Calculate([]);

        k.Plans.ShouldBe(0);
        k.VehiclesUsed.ShouldBe(0);
        k.AverageWeightUtilisation.ShouldBeNull();
        k.CostPerTonneKm.ShouldBeNull();
        k.FtlPercent.ShouldBe(0);
        k.Daily.ShouldBeEmpty();
    }

    [Fact]
    public void Counts_cost_and_savings_add_up_across_plans()
    {
        var k = PlanningKpiCalculator.Calculate([
            Plan(D1, PlanStatus.Approved, [Vehicle(cost: 10_000m, deliveries: 2, consolidation: 5_000m), Vehicle(cost: 8_000m, backhaul: 3_000m, returns: 1)]),
            Plan(D1.AddDays(1), PlanStatus.Completed, [Vehicle(cost: 2_000m)], [new UnplannedOrder(Guid.NewGuid(), "U1", UnplannedCodes.NoRate, "no rate", [])]),
        ]);

        k.Plans.ShouldBe(2);
        k.VehiclesUsed.ShouldBe(3);
        k.OrdersPlanned.ShouldBe(5); // 2 + (1 + 1 return) + 1
        k.OrdersUnplanned.ShouldBe(1);
        k.OrdersTotal.ShouldBe(6);
        k.TotalFreightCost.ShouldBe(20_000m);
        k.ConsolidationSaving.ShouldBe(5_000m);
        k.BackhaulSaving.ShouldBe(3_000m);
        k.TotalSavings.ShouldBe(8_000m);
    }

    [Fact]
    public void Weight_and_volume_utilisation_are_separate_averages_over_vehicles_that_have_them()
    {
        var k = PlanningKpiCalculator.Calculate([Plan(D1, PlanStatus.Completed, [
            Vehicle(weightUtil: 0.8m, volumeUtil: 0.6m), Vehicle(weightUtil: 0.4m, volumeUtil: 0.2m), Vehicle(FreightMode.Ptl, weightUtil: null, volumeUtil: null)])]);

        k.AverageWeightUtilisation.ShouldBe(0.6m);
        k.AverageVolumeUtilisation.ShouldBe(0.4m);
    }

    [Fact]
    public void Mode_consolidation_and_return_shares_are_percentages_of_the_right_base()
    {
        var k = PlanningKpiCalculator.Calculate([Plan(D1, PlanStatus.Completed, [
            Vehicle(deliveries: 3), Vehicle(returns: 1), Vehicle(FreightMode.Ptl), Vehicle(FreightMode.Ptl)])]);

        k.FtlPercent.ShouldBe(50m);
        k.PtlPercent.ShouldBe(50m);
        k.ConsolidatedPercent.ShouldBe(25m); // one of four vehicles carries more than one delivery
        k.ReturnPickupPercent.ShouldBe(14.3m); // one return among seven planned orders (3 + 2 + 1 + 1)
    }

    [Fact]
    public void Cost_per_tonne_km_uses_only_vehicles_with_a_measured_distance()
    {
        var k = PlanningKpiCalculator.Calculate([Plan(D1, PlanStatus.Completed, [
            Vehicle(cost: 10_000m, weightKg: 5_000m, km: 100), // 500 tonne-km
            Vehicle(cost: 99_999m, weightKg: 5_000m, km: null)])]);

        k.CostPerTonneKm.ShouldBe(20m);
        k.TotalDistanceKm.ShouldBe(100);
    }

    [Fact]
    public void The_daily_series_groups_by_planning_date_and_reasons_are_ranked()
    {
        var u = (string code) => new UnplannedOrder(Guid.NewGuid(), "U", code, "x", []);
        var k = PlanningKpiCalculator.Calculate([
            Plan(D1, PlanStatus.Completed, [Vehicle(cost: 1_000m)], [u(UnplannedCodes.NoRate), u(UnplannedCodes.NoRate), u(UnplannedCodes.PayloadExceeded)]),
            Plan(D1, PlanStatus.Completed, [Vehicle(cost: 2_000m)]),
            Plan(D1.AddDays(2), PlanStatus.Completed, [Vehicle(cost: 4_000m)]),
        ]);

        k.Daily.Select(d => (d.Date, d.Plans, d.Cost)).ToArray().ShouldBe([(D1, 2, 3_000m), (D1.AddDays(2), 1, 4_000m)]);
        k.UnplannedReasons.Select(r => (r.Code, r.Orders)).ToArray().ShouldBe([(UnplannedCodes.NoRate, 2), (UnplannedCodes.PayloadExceeded, 1)]);
    }

    [Fact]
    public void Spend_is_ranked_by_transporter_and_vehicles_by_type()
    {
        var k = PlanningKpiCalculator.Calculate([Plan(D1, PlanStatus.Completed, [
            Vehicle(cost: 5_000m, transporter: "A", type: "14ft"), Vehicle(cost: 9_000m, transporter: "B", type: "32ft"), Vehicle(cost: 3_000m, transporter: "A", type: "14ft")])]);

        k.Transporters.Select(t => (t.Name, t.Cost)).ToArray().ShouldBe([("B", 9_000m), ("A", 8_000m)]);
        k.VehicleTypes.Select(t => (t.Name, t.Vehicles)).ToArray().ShouldBe([("14ft", 2), ("32ft", 1)]);
    }
}
