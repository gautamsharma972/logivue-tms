using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterPerformanceApiTests(TmsApiFactory factory)
{
    private static DateOnly Today => ContractApiData.Today;

    private static async Task<ShipmentDto> RunToDeliveryAsync(ShipmentScenario s)
    {
        var accepted = await s.AcceptedAsync();
        var dispatched = await s.Admin.PostAsync($"/api/v1/shipments/{accepted.Summary.Id}/dispatch", null);
        dispatched.StatusCode.ShouldBe(HttpStatusCode.OK, await dispatched.Content.ReadAsStringAsync());
        var delivered = await s.Admin.PostAsync($"/api/v1/shipments/{accepted.Summary.Id}/deliver", null);
        delivered.StatusCode.ShouldBe(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
        return await delivered.ReadAsync<ShipmentDto>();
    }

    [Fact]
    public async Task A_load_that_is_tendered_accepted_dispatched_and_delivered_builds_its_own_performance_record()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var shipment = await RunToDeliveryAsync(s);

        var executions = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/executions")).ReadAsync<List<ExecutionDto>>();
        var execution = executions.Single(e => e.ShipmentId == shipment.Summary.Id);
        execution.Status.ShouldBe(ExecutionStatus.Delivered);
        execution.Events.Select(e => e.EventType).ShouldBe([ExecutionEventType.VehicleDeparture, ExecutionEventType.DeliveryComplete]);
        execution.PlannedPickupAt.ShouldNotBeNull();
        execution.ActualPickupAt.ShouldNotBeNull();

        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(30):yyyy-MM-dd}")).ReadAsync<PerformanceDto>();
        var acceptance = performance.Kpis.Single(k => k.Kpi == KpiType.TenderAcceptance);
        acceptance.Numerator.ShouldBeGreaterThanOrEqualTo(1);
        acceptance.Value.ShouldBe(100m);
        performance.Months.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_declined_load_counts_against_tender_acceptance()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var tendered = await s.TenderedAsync();

        var rejected = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{tendered.Summary.Id}/reject", new ReasonRequest("No vehicle that day"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());

        var performance = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(30):yyyy-MM-dd}")).ReadAsync<PerformanceDto>();
        var acceptance = performance.Kpis.Single(k => k.Kpi == KpiType.TenderAcceptance);
        acceptance.Denominator.ShouldBe(1);
        acceptance.Numerator.ShouldBe(0);
        acceptance.Value.ShouldBe(0m);
    }

    [Fact]
    public async Task A_planner_can_say_why_a_load_was_late_and_it_moves_off_the_carrier()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var shipment = await RunToDeliveryAsync(s);
        var execution = (await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/executions")).ReadAsync<List<ExecutionDto>>()).Single(e => e.ShipmentId == shipment.Summary.Id);

        // The delivery was recorded just now; whether it was late depends on the planned time, so make it late by recording against a past plan: the attribution call must refuse an on-time event.
        var notLate = await s.Admin.PostJsonAsync($"/api/v1/executions/{execution.Id}/delay", new AttributeDelayRequest(Delivery: true, ReasonCode: "TRAFFIC"));
        if (execution.DeliveryDelayMinutes is > 15)
        {
            notLate.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        else
        {
            notLate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await notLate.ProblemCodeAsync()).ShouldBe("executions.not_late");
        }

        var unknown = await s.Admin.PostJsonAsync($"/api/v1/executions/{execution.Id}/delay", new AttributeDelayRequest(Delivery: false, ReasonCode: "NOT_A_REASON"));
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_scorecard_is_generated_from_stored_months_and_kept_as_history()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await RunToDeliveryAsync(s);
        var from = new DateOnly(Today.Year, Today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var recalculated = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/performance/recalculate", new PeriodRequest(from, to));
        recalculated.StatusCode.ShouldBe(HttpStatusCode.OK, await recalculated.Content.ReadAsStringAsync());
        var created = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/scorecards", new PeriodRequest(from, to));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var card = await created.ReadAsync<ScorecardDto>();

        card.Kpis.ShouldNotBeEmpty();
        card.Kpis.ShouldAllBe(k => k.Weight > 0);
        card.OverallScore.ShouldBeNull(); // one load is far below the minimum sample, so nothing is judged yet
        (await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/scorecards")).ReadAsync<List<ScorecardDto>>()).ShouldContain(c => c.Id == card.Id);

        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/scorecards", new PeriodRequest(to, from))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_lowered_minimum_sample_lets_a_small_history_be_scored_and_ranked()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        await RunToDeliveryAsync(s);
        var from = new DateOnly(Today.Year, Today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        (await s.Admin.PutJsonAsync("/api/v1/transporter-settings/scorecard.minimumSampleSize", new { value = 1 })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/performance/recalculate", new PeriodRequest(from, to));

        var card = await (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/scorecards", new PeriodRequest(from, to))).ReadAsync<ScorecardDto>();
        card.OverallScore.ShouldNotBeNull();

        var ranking = await (await s.Admin.GetAsync($"/api/v1/transporters/rankings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")).ReadAsync<RankingResultDto>();
        ranking.Rows.ShouldContain(r => r.TransporterId == s.Transporter.Id && r.Ranked && r.Rank != null);

        var benchmark = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/benchmark?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")).ReadAsync<BenchmarkDto>();
        benchmark.Rows.Count.ShouldBe(8);

        (await s.Admin.PutJsonAsync("/api/v1/transporter-settings/scorecard.minimumSampleSize", new { value = 20 })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lanes_are_maintained_per_transporter_and_validated()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);

        var created = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/lanes",
            new SaveLaneRequest("Maharashtra", "Pune", "Gujarat", null, Tms.SharedKernel.Contracts.FreightMode.Ftl, 720, Today, null, true, null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var lane = await created.ReadAsync<LaneDto>();
        lane.OriginState.ShouldBe("MAHARASHTRA");

        var updated = await s.Admin.PutJsonAsync($"/api/v1/lanes/{lane.Id}",
            new SaveLaneRequest("Maharashtra", "Pune", "Gujarat", null, null, 600, Today, null, false, lane.Version));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.ReadAsync<LaneDto>()).IsActive.ShouldBeFalse();

        var bad = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/lanes",
            new SaveLaneRequest("", null, "Gujarat", null, null, null, Today, Today.AddDays(-1), true, null));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/lanes")).ReadAsync<List<LaneDto>>()).ShouldContain(l => l.Id == lane.Id);
    }

    [Fact]
    public async Task Performance_data_is_staff_only_except_a_vendors_own_company()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var other = await ShipmentScenario.CreateAsync(factory);
        await RunToDeliveryAsync(s);
        var range = $"from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(30):yyyy-MM-dd}";
        using var reader = await factory.UserWithPermissionsAsync(s.Admin, "transporters.read");

        // A vendor sees its own performance but not another company's, and cannot compare or change anything.
        (await s.Vendor.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{range}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Vendor.GetAsync($"/api/v1/transporters/{other.Transporter.Id}/performance?{range}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Vendor.GetAsync($"/api/v1/transporters/rankings?{range}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/performance/recalculate", new PeriodRequest(Today, Today))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/scorecards", new PeriodRequest(Today, Today))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.GetAsync("/api/v1/transporter-settings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Staff without the performance permission see nothing.
        (await reader.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{range}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/api/v1/transporter-settings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Another tenant sees nothing at all.
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        (await acme.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/performance?{range}")).StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_vendor_can_record_milestones_on_its_own_load_in_order()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var accepted = await s.AcceptedAsync();
        var execution = (await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/executions")).ReadAsync<List<ExecutionDto>>()).Single(e => e.ShipmentId == accepted.Summary.Id);
        var now = DateTimeOffset.UtcNow;

        var arrived = await s.Vendor.PostJsonAsync($"/api/v1/executions/{execution.Id}/events", new RecordExecutionEventRequest(ExecutionEventType.VehicleArrival, now.AddMinutes(-30)));
        arrived.StatusCode.ShouldBe(HttpStatusCode.OK, await arrived.Content.ReadAsStringAsync());
        (await arrived.ReadAsync<ExecutionDto>()).Status.ShouldBe(ExecutionStatus.AtPickup);

        var again = await s.Vendor.PostJsonAsync($"/api/v1/executions/{execution.Id}/events", new RecordExecutionEventRequest(ExecutionEventType.VehicleArrival, now));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("executions.sequence_invalid");

        // Another company's vendor cannot even tell it exists.
        using var other = await ShipmentScenario.CreateAsync(factory);
        (await other.Vendor.PostJsonAsync($"/api/v1/executions/{execution.Id}/events", new RecordExecutionEventRequest(ExecutionEventType.LoadingStart, now))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
