using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlanEditApiTests(TmsApiFactory factory)
{
    private static readonly PlanOptions Separate = new(AllowPtl: false, AllowConsolidation: false);

    private static async Task<RunDto> TwoVehiclesAsync(ShipmentScenario s, decimal firstKg, decimal secondKg)
    {
        var a = await s.OrderAsync(firstKg, "Drop A");
        var b = await s.OrderAsync(secondKg, "Drop B");
        var response = await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [a.Id, b.Id], Separate));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var run = await response.ReadAsync<RunDto>();
        run.Plan.Vehicles.Count.ShouldBe(2);
        return run;
    }

    private static Task<HttpResponseMessage> Edit(ShipmentScenario s, RunDto run, EditPlanRequest edit) =>
        s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/edit", edit with { Comment = edit.Comment ?? "Planner decision in test" });

    [Fact]
    public async Task Moving_an_order_onto_another_truck_re_prices_it_and_creates_a_new_version()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var (first, second) = (run.Plan.Vehicles[0], run.Plan.Vehicles[1]);

        var response = await Edit(s, run, new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: second.Orders[0].OrderId, ToVehicleKey: first.Key, Comment: "Same lane, one truck is enough"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var edited = await response.ReadAsync<RunDto>();
        edited.PlanVersion.ShouldBe(2);
        edited.Number.ShouldBe(run.Number);
        edited.Reason.ShouldNotBeNull().ShouldContain("Moved");
        edited.Reason.ShouldContain("Same lane");
        var vehicle = edited.Plan.Vehicles.ShouldHaveSingleItem();
        vehicle.Orders.Count.ShouldBe(2);
        vehicle.WeightKg.ShouldBe(9_000m);
        vehicle.EstimatedCost.ShouldBe(s.FlatRate);
        edited.Plan.Summary.TotalCost.ShouldBeLessThan(run.Plan.Summary.TotalCost);

        (await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}")).ReadAsync<RunDto>()).Plan.Vehicles.Count.ShouldBe(2); // v1 untouched
    }

    [Fact]
    public async Task A_manual_change_needs_a_reason_except_a_plain_reorder()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var request = new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: run.Plan.Vehicles[1].Orders[0].OrderId, ToVehicleKey: run.Plan.Vehicles[0].Key);

        var response = await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/edit", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("comment");
    }

    [Fact]
    public async Task A_move_that_overloads_the_truck_is_refused_with_the_amount_and_nothing_is_saved()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 10_000m, 9_000m);

        var response = await Edit(s, run, new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: run.Plan.Vehicles[1].Orders[0].OrderId, ToVehicleKey: run.Plan.Vehicles[0].Key));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("planning.edit_invalid");
        (await response.Content.ReadAsStringAsync()).ShouldContain("3000");

        var versions = await (await s.Admin.GetAsync($"/api/v1/planning/runs/{run.Id}/versions")).ReadAsync<List<VersionDto>>();
        versions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Removing_an_order_makes_it_unplanned_with_a_reason_and_it_can_be_added_back()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var orderId = run.Plan.Vehicles[1].Orders[0].OrderId;

        var removed = await (await Edit(s, run, new EditPlanRequest(PlanEditKind.RemoveOrder, OrderId: orderId))).ReadAsync<RunDto>();

        removed.Plan.Vehicles.Count.ShouldBe(1);
        removed.Plan.Unplanned.ShouldHaveSingleItem().Code.ShouldBe(UnplannedCodes.RemovedByPlanner);
        removed.Status.ShouldBe(PlanStatus.PartiallyPlanned);

        var added = await Edit(s, removed, new EditPlanRequest(PlanEditKind.AddOrder, OrderId: orderId, ToVehicleKey: removed.Plan.Vehicles[0].Key));
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var back = await added.ReadAsync<RunDto>();
        back.Plan.Unplanned.ShouldBeEmpty();
        back.Plan.Vehicles.ShouldHaveSingleItem().Orders.Count.ShouldBe(2);
        back.Status.ShouldBe(PlanStatus.Completed);
    }

    [Fact]
    public async Task Stops_can_be_reordered_and_the_order_must_list_each_stop_once()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(2_000m, "Drop A");
        var b = await s.OrderAsync(2_000m, "Drop B");
        var run = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [a.Id, b.Id], new PlanOptions(AllowPtl: false)))).ReadAsync<RunDto>();
        var vehicle = run.Plan.Vehicles.ShouldHaveSingleItem();
        var ids = vehicle.Orders.OrderBy(o => o.Sequence).Select(o => o.OrderId).ToList();

        var bad = await Edit(s, run, new EditPlanRequest(PlanEditKind.ReorderStops, VehicleKey: vehicle.Key, OrderIds: [ids[0], ids[0]]));
        bad.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var reversed = await Edit(s, run, new EditPlanRequest(PlanEditKind.ReorderStops, VehicleKey: vehicle.Key, OrderIds: [ids[1], ids[0]]));
        reversed.StatusCode.ShouldBe(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await reversed.ReadAsync<RunDto>()).Plan.Vehicles.Single().Orders.OrderBy(o => o.Sequence).Select(o => o.OrderId).ToList().ShouldBe([ids[1], ids[0]]);
    }

    [Fact]
    public async Task Changing_to_a_vehicle_type_that_cannot_carry_the_load_is_refused()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var types = await (await s.Admin.GetAsync("/api/v1/planning/vehicle-types")).ReadAsync<List<Tms.SharedKernel.Contracts.VehicleTypeInfo>>();
        var tiny = types.OrderBy(t => t.PayloadKg).First();

        var response = await Edit(s, run, new EditPlanRequest(PlanEditKind.ChangeVehicleType, VehicleKey: run.Plan.Vehicles[0].Key, VehicleTypeId: tiny.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Payload exceeded");
    }

    [Fact]
    public async Task A_locked_vehicle_cannot_be_edited_until_it_is_unlocked()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var lockedKey = run.Plan.Vehicles[0].Key;
        await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/lock", new LockRequest(lockedKey, true));

        var response = await Edit(s, run, new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: run.Plan.Vehicles[1].Orders[0].OrderId, ToVehicleKey: lockedKey));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("locked");
    }

    private static Task<HttpResponseMessage> Lock(ShipmentScenario s, RunDto run, LockRequest request) =>
        s.Admin.PostJsonAsync($"/api/v1/planning/runs/{run.Id}/lock", request);

    [Fact]
    public async Task A_locked_order_stays_on_its_vehicle_until_unlocked()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var (first, second) = (run.Plan.Vehicles[0], run.Plan.Vehicles[1]);
        var orderId = second.Orders[0].OrderId;

        var locked = await Lock(s, run, new LockRequest(second.Key, true, LockKind.Order, orderId));
        locked.StatusCode.ShouldBe(HttpStatusCode.OK, await locked.Content.ReadAsStringAsync());
        var lockedRun = await locked.ReadAsync<RunDto>();
        lockedRun.Plan.Vehicles.Single(v => v.Key == second.Key).Orders[0].IsLocked.ShouldBeTrue();

        var move = await Edit(s, lockedRun, new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: orderId, ToVehicleKey: first.Key));
        move.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await move.Content.ReadAsStringAsync()).ShouldContain("locked to its vehicle");
        (await Edit(s, lockedRun, new EditPlanRequest(PlanEditKind.RemoveOrder, OrderId: orderId))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await Lock(s, lockedRun, new LockRequest(second.Key, false, LockKind.Order, orderId));
        var freed = await Edit(s, lockedRun, new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: orderId, ToVehicleKey: first.Key));
        freed.StatusCode.ShouldBe(HttpStatusCode.OK, await freed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_locked_sequence_cannot_be_reordered_and_survives_other_edits()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var a = await s.OrderAsync(2_000m, "Same Drop");
        var b = await s.OrderAsync(2_000m, "Same Drop");
        var run = await (await s.Admin.PostJsonAsync("/api/v1/planning/runs", new CreateRunRequest(ContractApiData.Today, [a.Id, b.Id], new PlanOptions(AllowPtl: false)))).ReadAsync<RunDto>();
        var vehicle = run.Plan.Vehicles.ShouldHaveSingleItem();
        vehicle.Orders.Count.ShouldBe(2);

        var locked = await (await Lock(s, run, new LockRequest(vehicle.Key, true, LockKind.Sequence))).ReadAsync<RunDto>();
        var ids = vehicle.Orders.OrderBy(o => o.Sequence).Select(o => o.OrderId).ToList();

        var reorder = await Edit(s, locked, new EditPlanRequest(PlanEditKind.ReorderStops, VehicleKey: vehicle.Key, OrderIds: [ids[1], ids[0]]));
        reorder.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reorder.Content.ReadAsStringAsync()).ShouldContain("sequence");

        var removed = await Edit(s, locked, new EditPlanRequest(PlanEditKind.RemoveOrder, OrderId: ids[1]));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync());
        (await removed.ReadAsync<RunDto>()).Plan.Vehicles.ShouldHaveSingleItem().SequenceLocked.ShouldBeTrue();
    }

    [Fact]
    public async Task A_locked_assignment_keeps_its_vehicle_and_driver_through_edits_and_blocks_a_type_change()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var (first, second) = (run.Plan.Vehicles[0], run.Plan.Vehicles[1]);

        var locked = await (await Lock(s, run, new LockRequest(first.Key, true, LockKind.Assignment))).ReadAsync<RunDto>();
        var types = await (await s.Admin.GetAsync("/api/v1/planning/vehicle-types")).ReadAsync<List<Tms.SharedKernel.Contracts.VehicleTypeInfo>>();
        var change = await Edit(s, locked, new EditPlanRequest(PlanEditKind.ChangeVehicleType, VehicleKey: first.Key, VehicleTypeId: types.OrderByDescending(t => t.PayloadKg).First().Id));
        change.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var moved = await Edit(s, locked, new EditPlanRequest(PlanEditKind.MoveOrder, OrderId: second.Orders[0].OrderId, ToVehicleKey: first.Key));
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var after = (await moved.ReadAsync<RunDto>()).Plan.Vehicles.ShouldHaveSingleItem();
        after.AssignedVehicle!.Id.ShouldBe(first.AssignedVehicle!.Id);
        after.AssignedDriver!.Id.ShouldBe(first.AssignedDriver!.Id);
        after.AssignmentLocked.ShouldBeTrue();
    }

    [Fact]
    public async Task A_re_plan_carries_over_anything_that_is_locked_at_any_level()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var keep = run.Plan.Vehicles[0];
        var locked = await (await Lock(s, run, new LockRequest(keep.Key, true, LockKind.Sequence))).ReadAsync<RunDto>();

        var replanned = await (await s.Admin.PostJsonAsync($"/api/v1/planning/runs/{locked.Id}/reoptimize", new ReoptimizeRequest("Try again", null))).ReadAsync<RunDto>();

        var carried = replanned.Plan.Vehicles.Single(v => v.Key == keep.Key);
        carried.SequenceLocked.ShouldBeTrue();
        carried.Orders.Select(o => o.OrderId).ShouldBe(keep.Orders.Select(o => o.OrderId));
        carried.AssignedVehicle!.Id.ShouldBe(keep.AssignedVehicle!.Id);
        replanned.Plan.Vehicles.Select(v => v.AssignedVehicle!.Id).Distinct().Count().ShouldBe(replanned.Plan.Vehicles.Count); // still no double-booking
    }

    [Fact]
    public async Task Only_the_latest_editable_version_can_be_edited_and_only_by_planners()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var run = await TwoVehiclesAsync(s, 4_000m, 5_000m);
        var orderId = run.Plan.Vehicles[1].Orders[0].OrderId;
        var edited = await (await Edit(s, run, new EditPlanRequest(PlanEditKind.RemoveOrder, OrderId: orderId))).ReadAsync<RunDto>();

        (await Edit(s, run, new EditPlanRequest(PlanEditKind.RemoveOrder, OrderId: run.Plan.Vehicles[0].Orders[0].OrderId))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // v1 is no longer latest
        using var reader = await factory.UserWithPermissionsAsync(s.Admin, "shipments.read");
        (await reader.PostJsonAsync($"/api/v1/planning/runs/{edited.Id}/edit", new EditPlanRequest(PlanEditKind.AddOrder, OrderId: orderId, Comment: "Add back"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await s.Admin.PostAsync($"/api/v1/planning/runs/{edited.Id}/approve", null);
        (await Edit(s, edited, new EditPlanRequest(PlanEditKind.AddOrder, OrderId: orderId))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // approved plans are frozen
    }
}
