using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Execution;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Pod;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>
/// Milestone 6: vehicle placement, pickup and delivery monitoring with delay attribution, POD lifecycle and
/// compliance, and the KPI pipeline (numerators, denominators, Not Measurable, recalculation).
/// </summary>
public class MilestoneSixTests : IClassFixture<TestHost>
{
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";

    // Historical dates keep KPI buckets isolated from current-month data. Each test uses its own transporter.
    private static readonly DateTime PastPickup = new(2025, 3, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PastDelivery = new(2025, 3, 11, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly MarchStart = new(2025, 3, 1);
    private static readonly DateOnly MarchEnd = new(2025, 3, 31);

    private readonly TestHost _host;
    private readonly HttpClient _admin;

    public MilestoneSixTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Internal("ops", AllRoles);
    }

    private sealed record AcceptedLoad(long TransporterId, long InvitationId, string LoadReference, string Registration, FixtureLane Lane);

    // ---------- clients & arrangements ----------

    private HttpClient Internal(string user, string roles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, user);
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private HttpClient Vendor(long transporterId)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, $"vendor-{transporterId}");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.TransporterHeader, transporterId.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, "Transporter Admin,Transporter Operations User,Transporter Viewer");
        return client;
    }

    /// <summary>Seeds an eligible transporter, tenders it a load, and (optionally) has it accepted. Times are set directly.</summary>
    private async Task<AcceptedLoad> AcceptedLoadAsync(DateTime pickup, DateTime delivery, bool accept = true)
    {
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedDetailedAsync(_host, lane, new FixtureProfile($"Ops {Guid.NewGuid():N}"[..14], 42000m));
        var tender = await _admin.PostAsJsonAsync("/api/v1/tenders", new CreateTenderRequest(TenderType.Direct, $"LD-{Guid.NewGuid():N}"[..12],
            lane.Origin, lane.Destination, "FTL", lane.VehicleType, 12000m, null,
            DateTime.UtcNow.AddDays(5), DateTime.UtcNow.AddDays(7), DateTime.UtcNow.AddHours(4), 42000m, "INR", [seeded.Id], null), TestHost.Json);
        tender.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = (await tender.Content.ReadFromJsonAsync<List<TenderInvitationDto>>(TestHost.Json))!.Single();

        await _admin.PostAsync($"/api/v1/tenders/{invitation.Id}/send", null);
        if (accept)
        {
            await Vendor(seeded.Id).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);
        }

        await _host.WithTransporterDbAsync(async db =>
        {
            var row = await db.Tenders.SingleAsync(t => t.Id == invitation.Id);
            row.PickupDateTime = pickup;
            row.DeliveryDateTime = delivery;
            await db.SaveChangesAsync();
            return true;
        });

        return new AcceptedLoad(seeded.Id, invitation.Id, invitation.LoadReference, seeded.VehicleRegistration, lane);
    }

    private async Task<string> AddVehicleAsync(AcceptedLoad load)
    {
        var registration = $"MH{Random.Shared.Next(10, 99)}{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await _host.WithTransporterDbAsync(async db =>
        {
            db.TransporterVehicles.Add(new TransporterVehicle
            {
                TransporterId = load.TransporterId, RegistrationNumber = registration, VehicleTypeReference = load.Lane.VehicleType,
                PayloadCapacityKg = 12000m, OwnershipType = VehicleOwnershipType.Owned,
                AvailabilityStatus = VehicleAvailabilityStatus.Available, Status = RecordStatus.Active
            });
            await db.SaveChangesAsync();
            return true;
        });
        return registration;
    }

    private async Task<HttpResponseMessage> AssignVehicleAsync(AcceptedLoad load, string registration) =>
        await Vendor(load.TransporterId).PostAsJsonAsync($"/api/v1/vendor/loads/{load.InvitationId}/vehicle",
            new VehicleAssignmentRequest(registration, "Amara Okafor", "+91 98200 55555", null, null), TestHost.Json);

    private async Task<PlacementDto> RequestPlacementAsync(AcceptedLoad load, DateTime requiredAt)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/vehicle-placement",
            new CreatePlacementRequest(load.LoadReference, load.TransporterId, requiredAt), TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PlacementDto>(TestHost.Json))!;
    }

    private async Task<LoadExecutionDto> CreateExecutionAsync(AcceptedLoad load, bool podRequired = true)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/executions",
            new CreateExecutionRequest(load.LoadReference, load.TransporterId, podRequired), TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LoadExecutionDto>(TestHost.Json))!;
    }

    private async Task<HttpResponseMessage> RecordAsync(long executionId, ExecutionEventType type, DateTime at, string? reason = null) =>
        await _admin.PostAsJsonAsync($"/api/v1/executions/{executionId}/events",
            new RecordExecutionEventRequest(type, at, reason, null), TestHost.Json);

    private async Task<LoadExecutionDto> RecordOkAsync(long executionId, ExecutionEventType type, DateTime at, string? reason = null)
    {
        var response = await RecordAsync(executionId, type, at, reason);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoadExecutionDto>(TestHost.Json))!;
    }

    private async Task<HttpResponseMessage> SubmitPodAsync(AcceptedLoad load, string fileName = "pod.pdf", string contentType = "application/pdf", DateOnly? podDate = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        form.Add(new StringContent((podDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyy-MM-dd")), "PodDate");
        form.Add(new StringContent("Gate guard"), "ReceivedBy");
        return await Vendor(load.TransporterId).PostAsync($"/api/v1/vendor/loads/{load.LoadReference}/pod", form);
    }

    private async Task<PodDto> PodForLoadAsync(AcceptedLoad load)
    {
        var pods = (await _admin.GetFromJsonAsync<LogiVue.Tms.Shared.Common.PagedResult<PodDto>>($"/api/v1/pods?transporterId={load.TransporterId}", TestHost.Json))!.Items;
        return pods!.Single();
    }

    private async Task<OperationalPeriodResult> OperationsAsync(long transporterId, DateOnly from, DateOnly to) =>
        (await _admin.GetFromJsonAsync<OperationalPeriodResult>(
            $"/api/v1/transporters/performance/{transporterId}/operations?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", TestHost.Json))!;

    private async Task<ApiError> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiError>(TestHost.Json))!;

    // ---------- vehicle placement ----------

    [Fact]
    public async Task Placement_runs_from_confirmation_to_placed_on_time()
    {
        var load = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4));
        var placement = await RequestPlacementAsync(load, DateTime.UtcNow.AddDays(1));
        var vendor = Vendor(load.TransporterId);

        (await AssignVehicleAsync(load, load.Registration)).StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await vendor.PostAsync($"/api/v1/vendor/placements/{placement.Id}/confirm", null);
        var reported = await vendor.PostAsync($"/api/v1/vendor/placements/{placement.Id}/report", null);
        var placed = await _admin.PostAsync($"/api/v1/vehicle-placement/{placement.Id}/place", null);

        var dto = await placed.Content.ReadFromJsonAsync<PlacementDto>(TestHost.Json);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        reported.StatusCode.Should().Be(HttpStatusCode.OK);
        dto!.Status.Should().Be(PlacementStatus.Placed);
        dto.SlaStatus.Should().Be("OnTime");
        dto.VehicleRegistration.Should().Be(load.Registration);
        dto.Events.Select(e => e.EventType).Should().ContainInOrder("Requested", "Confirmed", "VehicleAssigned", "Reported", "Placed");
    }

    [Fact]
    public async Task Placement_requires_a_load_the_transporter_has_accepted()
    {
        var load = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4), accept: false);

        var response = await _admin.PostAsJsonAsync("/api/v1/vehicle-placement",
            new CreatePlacementRequest(load.LoadReference, load.TransporterId, DateTime.UtcNow.AddDays(1)), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("PLACEMENT_REQUIRES_ACCEPTED_LOAD");
    }

    [Fact]
    public async Task Vendor_cannot_confirm_another_transporters_placement()
    {
        var owner = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4));
        var other = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4));
        var placement = await RequestPlacementAsync(owner, DateTime.UtcNow.AddDays(1));

        var response = await Vendor(other.TransporterId).PostAsync($"/api/v1/vendor/placements/{placement.Id}/confirm", null);
        var ownPlacements = (await Vendor(owner.TransporterId).GetFromJsonAsync<LogiVue.Tms.Shared.Common.PagedResult<PlacementDto>>("/api/v1/vendor/placements", TestHost.Json))!.Items;

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ownPlacements!.Should().ContainSingle(p => p.Id == placement.Id);
    }

    [Fact]
    public async Task No_show_is_refused_inside_the_grace_period_and_recorded_once_overdue()
    {
        var early = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4));
        var notDue = await RequestPlacementAsync(early, DateTime.UtcNow.AddDays(1));
        var refused = await _admin.PostAsJsonAsync($"/api/v1/vehicle-placement/{notDue.Id}/no-show", new ReasonRequest("Vehicle did not arrive"), TestHost.Json);

        var overdue = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4));
        var missed = await RequestPlacementAsync(overdue, DateTime.UtcNow.AddHours(-1));
        var recorded = await _admin.PostAsJsonAsync($"/api/v1/vehicle-placement/{missed.Id}/no-show", new ReasonRequest("Vehicle did not arrive"), TestHost.Json);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(refused)).Code.Should().Be("NO_SHOW_TOO_EARLY");
        var dto = await recorded.Content.ReadFromJsonAsync<PlacementDto>(TestHost.Json);
        dto!.Status.Should().Be(PlacementStatus.NoShow);
        dto.SlaStatus.Should().Be("NoShow");

        var alert = await _host.WithTransporterDbAsync(db => db.Alerts.AnyAsync(a => a.TransporterId == overdue.TransporterId && a.AlertType == "PLACEMENT_NO_SHOW"));
        alert.Should().BeTrue();
    }

    [Fact]
    public async Task Changing_the_assigned_vehicle_after_assignment_counts_as_a_replacement()
    {
        var load = await AcceptedLoadAsync(DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(4));
        var placement = await RequestPlacementAsync(load, DateTime.UtcNow.AddDays(1));
        await AssignVehicleAsync(load, load.Registration);
        await Vendor(load.TransporterId).PostAsync($"/api/v1/vendor/placements/{placement.Id}/confirm", null);

        var replacement = await AddVehicleAsync(load);
        (await AssignVehicleAsync(load, replacement)).StatusCode.Should().Be(HttpStatusCode.OK);

        var placements = (await _admin.GetFromJsonAsync<LogiVue.Tms.Shared.Common.PagedResult<PlacementDto>>($"/api/v1/vehicle-placement?transporterId={load.TransporterId}", TestHost.Json))!.Items;
        var updated = placements!.Single(p => p.Id == placement.Id);
        updated.ReplacementCount.Should().Be(1);
        updated.VehicleRegistration.Should().Be(replacement);
        updated.Status.Should().Be(PlacementStatus.VehicleAssigned);
        updated.Events.Should().Contain(e => e.EventType == "VehicleReplaced");
    }

    // ---------- pickup and delivery ----------

    [Fact]
    public async Task Execution_requires_an_accepted_load_and_events_follow_the_sequence()
    {
        var notAccepted = await AcceptedLoadAsync(PastPickup, PastDelivery, accept: false);
        var refused = await _admin.PostAsJsonAsync("/api/v1/executions", new CreateExecutionRequest(notAccepted.LoadReference, notAccepted.TransporterId), TestHost.Json);

        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.VehicleArrival, PastPickup.AddMinutes(-10));
        var repeated = await RecordAsync(execution.Id, ExecutionEventType.VehicleArrival, PastPickup.AddMinutes(-5));

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(refused)).Code.Should().Be("EXECUTION_REQUIRES_ACCEPTED_LOAD");
        repeated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(repeated)).Code.Should().Be("EXECUTION_SEQUENCE_INVALID");
    }

    [Fact]
    public async Task Late_pickup_caused_by_traffic_does_not_count_against_the_transporter()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        var recorded = await RecordOkAsync(execution.Id, ExecutionEventType.VehicleDeparture, PastPickup.AddMinutes(90), "TRAFFIC");

        var operations = await OperationsAsync(load.TransporterId, MarchStart, MarchEnd);

        recorded.PickupAttribution.Should().Be(DelayAttribution.NonCarrier);
        recorded.PickupDelayMinutes.Should().Be(90);
        var pickup = operations.Kpis.Single(k => k.Type == KpiType.OnTimePickup);
        pickup.Denominator.Should().Be(0);
        pickup.Value.Should().BeNull();
    }

    [Fact]
    public async Task Late_pickup_attributed_to_the_carrier_counts_as_a_failure()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        var recorded = await RecordOkAsync(execution.Id, ExecutionEventType.VehicleDeparture, PastPickup.AddMinutes(120), "TRANSPORTER_DELAY");

        var operations = await OperationsAsync(load.TransporterId, MarchStart, MarchEnd);

        recorded.PickupAttribution.Should().Be(DelayAttribution.Carrier);
        var pickup = operations.Kpis.Single(k => k.Type == KpiType.OnTimePickup);
        pickup.Numerator.Should().Be(0);
        pickup.Denominator.Should().Be(1);
        pickup.Value.Should().Be(0m);
        operations.Metrics.LatePickupsCarrier.Should().Be(1);
    }

    [Fact]
    public async Task Late_delivery_without_a_reason_is_flagged_for_attribution_and_not_scored()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.VehicleDeparture, PastPickup.AddMinutes(5));
        var delivered = await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, PastDelivery.AddHours(3));

        var alert = await _host.WithTransporterDbAsync(db => db.Alerts.AnyAsync(a => a.LoadReference == load.LoadReference && a.AlertType == "DELAY_ATTRIBUTION_REQUIRED"));
        var operations = await OperationsAsync(load.TransporterId, MarchStart, MarchEnd);

        delivered.DeliveryAttribution.Should().Be(DelayAttribution.Unattributed);
        alert.Should().BeTrue();
        operations.Kpis.Single(k => k.Type == KpiType.OnTimeDelivery).Denominator.Should().Be(0);
        operations.Metrics.LateDeliveriesUnattributed.Should().Be(1);
    }

    [Fact]
    public async Task Unknown_delay_reasons_are_refused()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);

        var response = await RecordAsync(execution.Id, ExecutionEventType.VehicleDeparture, PastPickup.AddMinutes(60), "BECAUSE");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("DELAY_REASON_INVALID");
    }

    // ---------- POD lifecycle ----------

    [Fact]
    public async Task Delivery_opens_a_pod_that_the_vendor_submits_within_sla_and_the_portal_reflects_it()
    {
        var recent = DateTime.UtcNow.AddHours(-1);
        var load = await AcceptedLoadAsync(recent.AddHours(-4), recent);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, recent);
        var vendor = Vendor(load.TransporterId);

        var pending = await vendor.GetFromJsonAsync<VendorDashboardDto>("/api/v1/vendor/dashboard", TestHost.Json);
        var submitted = await SubmitPodAsync(load);
        var podDto = await submitted.Content.ReadFromJsonAsync<PodDto>(TestHost.Json);
        var afterSubmit = await vendor.GetFromJsonAsync<VendorDashboardDto>("/api/v1/vendor/dashboard", TestHost.Json);
        var loads = await vendor.GetFromJsonAsync<List<VendorLoadDto>>("/api/v1/vendor/loads", TestHost.Json);

        pending!.PendingPod.Should().Be(1);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        podDto!.Status.Should().Be(PodStatus.Submitted);
        podDto.SubmissionSla.Should().Be("OnTime");
        afterSubmit!.PendingPod.Should().Be(0);
        loads!.Single(l => l.LoadReference == load.LoadReference).LoadStatus.Should().Be("POD Pending");
    }

    [Fact]
    public async Task Late_submission_is_flagged_against_the_pod_sla()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, PastDelivery);

        var response = await SubmitPodAsync(load, podDate: new DateOnly(2025, 3, 11));
        var dto = await response.Content.ReadFromJsonAsync<PodDto>(TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        dto!.SubmittedWithinSla.Should().BeFalse();
        dto.SubmissionSla.Should().Be("Late");
    }

    [Fact]
    public async Task Pod_review_moves_through_rejection_and_resubmission()
    {
        var recent = DateTime.UtcNow.AddHours(-1);
        var load = await AcceptedLoadAsync(recent.AddHours(-4), recent);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, recent);
        var premature = await _admin.PostAsync($"/api/v1/pods/{(await PodForLoadAsync(load)).Id}/accept", null);
        await SubmitPodAsync(load);
        var pod = await PodForLoadAsync(load);

        var review = await _admin.PostAsync($"/api/v1/pods/{pod.Id}/start-review", null);
        var rejected = await _admin.PostAsJsonAsync($"/api/v1/pods/{pod.Id}/reject", new ReasonRequest("Signature missing"), TestHost.Json);
        var blockedResubmit = await SubmitPodAsync(load);
        var sentBack = await _admin.PostAsync($"/api/v1/pods/{pod.Id}/request-resubmission", null);
        var resubmitted = await SubmitPodAsync(load);
        var accepted = await _admin.PostAsync($"/api/v1/pods/{pod.Id}/accept", null);

        premature.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(premature)).Code.Should().Be("POD_ILLEGAL_TRANSITION");
        review.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejectedDto = await rejected.Content.ReadFromJsonAsync<PodDto>(TestHost.Json);
        rejectedDto!.Status.Should().Be(PodStatus.Rejected);
        rejectedDto.RejectionCount.Should().Be(1);
        rejectedDto.RejectionReason.Should().Be("Signature missing");
        blockedResubmit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(blockedResubmit)).Code.Should().Be("POD_NOT_SUBMITTABLE");
        sentBack.StatusCode.Should().Be(HttpStatusCode.OK);
        resubmitted.StatusCode.Should().Be(HttpStatusCode.OK);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<PodDto>(TestHost.Json))!.Status.Should().Be(PodStatus.Accepted);
    }

    [Fact]
    public async Task Vendor_cannot_download_another_transporters_pod()
    {
        var recent = DateTime.UtcNow.AddHours(-1);
        var load = await AcceptedLoadAsync(recent.AddHours(-4), recent);
        var intruder = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, recent);
        await SubmitPodAsync(load);
        var pod = await PodForLoadAsync(load);

        var ownFile = await Vendor(load.TransporterId).GetAsync($"/api/v1/vendor/pods/{pod.Id}/file");
        var foreignFile = await Vendor(intruder.TransporterId).GetAsync($"/api/v1/vendor/pods/{pod.Id}/file");

        ownFile.StatusCode.Should().Be(HttpStatusCode.OK);
        foreignFile.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Vendor_loads_show_the_execution_and_pod_stages()
    {
        var recent = DateTime.UtcNow.AddHours(-1);
        var load = await AcceptedLoadAsync(recent.AddHours(-4), recent);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.VehicleDeparture, recent.AddHours(-3));
        var vendor = Vendor(load.TransporterId);

        var inTransit = (await vendor.GetFromJsonAsync<List<VendorLoadDto>>("/api/v1/vendor/loads", TestHost.Json))!.Single(l => l.LoadReference == load.LoadReference).LoadStatus;
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, recent);
        await SubmitPodAsync(load);
        var pod = await PodForLoadAsync(load);
        await _admin.PostAsync($"/api/v1/pods/{pod.Id}/accept", null);
        var completed = (await vendor.GetFromJsonAsync<List<VendorLoadDto>>("/api/v1/vendor/loads", TestHost.Json))!.Single(l => l.LoadReference == load.LoadReference).LoadStatus;

        inTransit.Should().Be("In Transit");
        completed.Should().Be("Completed");
    }

    // ---------- KPI pipeline ----------

    [Fact]
    public async Task Recalculation_stores_auditable_kpis_and_is_repeatable()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.VehicleDeparture, PastPickup.AddMinutes(5));
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, PastDelivery.AddMinutes(5));

        var request = new RecalculationRequest(load.TransporterId, MarchStart, MarchEnd);
        var first = await _admin.PostAsJsonAsync("/api/v1/transporters/performance/recalculate", request, TestHost.Json);
        var second = await _admin.PostAsJsonAsync("/api/v1/transporters/performance/recalculate", request, TestHost.Json);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        // Fixture rows for the quarter are also stored for this transporter; only the March bucket is asserted here.
        var rows = (await _host.WithTransporterDbAsync(db => db.PerformanceKpis.AsNoTracking()
            .Where(k => k.TransporterId == load.TransporterId && k.KpiType == KpiType.OnTimeDelivery).ToListAsync()))
            .Where(k => k.PeriodStart == MarchStart && k.LaneReference == null && k.VehicleTypeReference == null).ToList();
        rows.Should().ContainSingle();
        rows[0].Numerator.Should().Be(1);
        rows[0].Denominator.Should().Be(1);
        rows[0].KpiValue.Should().Be(100m);
        rows[0].PeriodStart.Should().Be(MarchStart);
        rows[0].PeriodEnd.Should().Be(MarchEnd);

        // The same delivery is also stored against its lane, so the lane's own sample is auditable.
        var laneRows = await _host.WithTransporterDbAsync(db => db.PerformanceKpis.AsNoTracking()
            .Where(k => k.TransporterId == load.TransporterId && k.KpiType == KpiType.OnTimeDelivery && k.LaneReference != null).ToListAsync());
        laneRows.Should().ContainSingle(k => k.PeriodStart == MarchStart && k.Denominator == 1 && k.Numerator == 1);
    }

    [Fact]
    public async Task Missing_planned_delivery_is_not_measurable_rather_than_zero()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var execution = await CreateExecutionAsync(load);
        await RecordOkAsync(execution.Id, ExecutionEventType.DeliveryComplete, PastDelivery);
        await _host.WithTransporterDbAsync(async db =>
        {
            var row = await db.LoadExecutions.SingleAsync(e => e.Id == execution.Id);
            row.PlannedDeliveryAt = null;
            await db.SaveChangesAsync();
            return true;
        });

        var recalculated = await _admin.PostAsJsonAsync("/api/v1/transporters/performance/recalculate",
            new RecalculationRequest(load.TransporterId, MarchStart, MarchEnd), TestHost.Json);
        var otd = (await recalculated.Content.ReadFromJsonAsync<List<OperationalPeriodResult>>(TestHost.Json))!
            .Single().Kpis.Single(k => k.Type == KpiType.OnTimeDelivery);

        recalculated.StatusCode.Should().Be(HttpStatusCode.OK);
        otd.Denominator.Should().Be(0);
        otd.Value.Should().BeNull();
    }

    [Fact]
    public async Task Recalculation_rejects_an_unknown_transporter()
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/transporters/performance/recalculate",
            new RecalculationRequest(999_999_999, MarchStart, MarchEnd), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operational_endpoints_refuse_vendor_callers()
    {
        var load = await AcceptedLoadAsync(PastPickup, PastDelivery);
        var vendor = Vendor(load.TransporterId);

        var operations = await vendor.GetAsync($"/api/v1/transporters/performance/{load.TransporterId}/operations?from=2025-03-01&to=2025-03-31");
        var pods = await vendor.GetAsync("/api/v1/pods");

        operations.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        pods.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

}
