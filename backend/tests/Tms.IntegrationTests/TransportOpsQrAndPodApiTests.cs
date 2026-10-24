using System.Net;
using System.Text;
using System.Text.Json;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransportOpsQrApiTests(TmsApiFactory factory)
{
    private static async Task<(OpsScenario S, CreatedLrDto Lr, string Token, TripDetailDto Trip)> OnTheRoadAsync(TmsApiFactory factory)
    {
        var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();
        var trip = await s.CreateTripAsync(lr.Lr.Lr.Id);
        await s.DepartAsync(trip.Trip.Id);
        return (s, lr, OpsScenario.Token(lr), trip);
    }

    [Fact]
    public async Task A_valid_code_opens_the_right_lr_and_shows_only_what_a_driver_needs()
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var view = await s.ViewAsync(token);

        view.LrNumber.ShouldBe(lr.Lr.Lr.Number);
        view.NextAction.ShouldBe(QrActions.ConfirmArrival);
        view.VehicleRegistration.ShouldBe("MH12AB1234");
        view.CfaName.ShouldBe("Demo CFA Mumbai");
        view.DestinationName.ShouldBe("Demo Distributor Pune");
        view.Material.ShouldContain("Soap cartons");

        // Nothing commercial, and nothing about any other delivery, is on the page.
        using var doc = JsonDocument.Parse(await s.Anonymous.GetStringAsync(OpsScenario.Url(token)));
        doc.RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ShouldNotContain(n => n.Contains("freight") || n.Contains("rate") || n.Contains("price") || n.Contains("amount") || n.Contains("gstin") || n.Contains("token"));
    }

    [Fact]
    public async Task A_wrong_malformed_or_revoked_code_all_get_the_same_not_found_answer()
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var guesses = new[] { "short", new string('a', 43), Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=') + new string('x', 30) };
        var bodies = new List<string>();
        foreach (var guess in guesses)
        {
            var response = await s.Anonymous.GetAsync(OpsScenario.Url(guess));
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            bodies.Add(await response.ProblemCodeAsync() ?? string.Empty);
        }

        (await s.Admin.DeleteAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/qr")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var revoked = await s.Anonymous.GetAsync(OpsScenario.Url(token));
        revoked.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bodies.Add(await revoked.ProblemCodeAsync() ?? string.Empty);
        (await s.ArriveAtPuneAsync(token)).StatusCode.ShouldBe(HttpStatusCode.NotFound); // a revoked code can do nothing

        bodies.Distinct().Count().ShouldBe(1);
        (await s.Admin.GetAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/print")).StatusCode.ShouldBe(HttpStatusCode.Conflict); // nothing to print until a new code is issued
    }

    [Fact]
    public async Task Issuing_a_new_code_replaces_the_old_one_at_once()
    {
        var (s, lr, oldToken, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var fresh = await OpsScenario.Ok<CreatedLrDto>(s.Admin.PostAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/qr", null));

        fresh.QrPath.ShouldNotBe(lr.QrPath);
        (await s.Anonymous.GetAsync(OpsScenario.Url(oldToken))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Anonymous.GetAsync(OpsScenario.Url(OpsScenario.Token(fresh)))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Before_the_vehicle_leaves_the_cfa_the_page_offers_nothing_and_arrival_is_refused()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();
        var token = OpsScenario.Token(lr);

        var view = await s.ViewAsync(token);
        view.NextAction.ShouldBe(QrActions.None);
        view.Message.ShouldContain("not left");
        var response = await s.ArriveAtPuneAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("ops.arrival_state");
        (await s.UnloadAsync(token)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("none", "ops.arrival_no_location")]
    [InlineData("denied", "ops.arrival_permission_denied")]
    [InlineData("vague", "ops.arrival_accuracy")]
    [InlineData("far", "ops.arrival_outside")]
    [InlineData("stale", "ops.arrival_location_stale")]
    public async Task Arrival_is_refused_whenever_the_location_cannot_vouch_for_it_and_the_refusal_is_kept(string kind, string expectedCode)
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var response = kind switch
        {
            "none" => await s.ArriveAsync(token, null, null, null, error: "unavailable"),
            "denied" => await s.ArriveAsync(token, null, null, null, error: "denied"),
            "vague" => await s.ArriveAsync(token, 18.7606, 73.8636, 800),
            "far" => await s.ArriveAsync(token, 18.80, 73.9, 10), // several kilometres away
            _ => await s.ArriveAsync(token, 18.7607, 73.8637, 10, capturedAt: DateTimeOffset.UtcNow.AddMinutes(-30)),
        };

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe(expectedCode);
        var detail = await s.GetLrAsync(lr.Lr.Lr.Id);
        detail.Lr.Status.ShouldBe(DeliveryStatus.InTransit); // nothing moved
        detail.Lr.ArrivedAt.ShouldBeNull();
        detail.Events.ShouldContain(e => e.Type == EventType.ArrivalRejected && e.Source == EventSource.QrWeb);
        (await s.ViewAsync(token)).NextAction.ShouldBe(QrActions.ConfirmArrival); // the driver can try again
    }

    [Fact]
    public async Task A_good_fix_inside_the_radius_records_arrival_with_the_evidence_the_server_used()
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var response = await s.ArriveAtPuneAsync(token, "tap-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.ReadAsync<QrViewDto>();
        view.Status.ShouldBe(DeliveryStatus.Arrived);
        view.ArrivedAt.ShouldNotBeNull();
        view.NextAction.ShouldBe(QrActions.ConfirmUnloading);
        var arrival = (await s.GetLrAsync(lr.Lr.Lr.Id)).Events.Single(e => e.Type == EventType.DestinationArrival);
        arrival.Source.ShouldBe(EventSource.QrWeb);
        arrival.Verification.ShouldBe("Inside");
        arrival.RadiusM.ShouldBe(150); // the radius of that location, not a constant
        arrival.AccuracyM.ShouldBe(12);
        arrival.DistanceM.ShouldNotBeNull().ShouldBeLessThan(150);
    }

    [Fact]
    public async Task The_same_arrival_sent_twice_or_by_two_phones_at_once_is_recorded_once()
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var first = await s.ArriveAtPuneAsync(token, "same");
        var replay = await s.ArriveAtPuneAsync(token, "same");
        var other = await s.ArriveAtPuneAsync(token, "different");
        var burst = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => s.ArriveAtPuneAsync(token, $"burst-{i}")));

        new[] { first, replay, other }.Concat(burst).ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var events = (await s.GetLrAsync(lr.Lr.Lr.Id)).Events;
        events.Count(e => e.Type == EventType.DestinationArrival).ShouldBe(1);
        (await other.ReadAsync<QrViewDto>()).Message.ShouldContain("already");
    }

    [Fact]
    public async Task Unloading_waits_for_arrival_and_never_reads_a_location()
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;

        var early = await s.UnloadAsync(token);
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.ProblemCodeAsync()).ShouldBe("ops.unloading_state");
        (await s.ArriveAtPuneAsync(token)).EnsureSuccessStatusCode();

        // A location sent along is simply not part of this action: the server has nowhere to put it and never looks at one.
        var raw = new StringContent("""{"clientKey":"u1","latitude":10.0,"longitude":10.0,"accuracyM":9999}""", Encoding.UTF8, "application/json");
        var done = await s.Anonymous.PostAsync(OpsScenario.Url(token, "/unloading"), raw);

        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await done.ReadAsync<QrViewDto>();
        view.Status.ShouldBe(DeliveryStatus.UnloadingCompleted);
        view.PodStatus.ShouldBe(PodStatus.Pending);
        view.NextAction.ShouldBe(QrActions.UploadPod);
        var unloading = (await s.GetLrAsync(lr.Lr.Lr.Id)).Events.Single(e => e.Type == EventType.UnloadingCompleted);
        unloading.Verification.ShouldBeNull();
        unloading.DistanceM.ShouldBeNull();
        unloading.AccuracyM.ShouldBeNull();
        (await s.UnloadAsync(token, "u1")).StatusCode.ShouldBe(HttpStatusCode.OK); // a retry changes nothing
        (await s.UnloadAsync(token, "u2")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.GetLrAsync(lr.Lr.Lr.Id)).Events.Count(e => e.Type == EventType.UnloadingCompleted).ShouldBe(1);
    }

    [Fact]
    public async Task Only_arrival_can_carry_a_location_the_other_driver_actions_have_none_to_send()
    {
        foreach (var type in new[] { typeof(UnloadingRequest), typeof(PodPendingRequest) })
        {
            type.GetProperties().Select(p => p.Name.ToLowerInvariant()).ShouldNotContain(n => n.Contains("lat") || n.Contains("lon") || n.Contains("accuracy") || n.Contains("location"), type.Name);
        }

        typeof(ArrivalRequest).GetProperties().Select(p => p.Name).ShouldContain("Latitude");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_supervisor_can_record_an_arrival_with_a_reason_but_a_dispatcher_cannot()
    {
        var (s, lr, token, _) = await OnTheRoadAsync(factory);
        using var _s = s;
        using var dispatcher = await factory.UserWithPermissionsAsync(s.Admin, OpsPermissions.Dispatch, OpsPermissions.Read);

        var denied = await dispatcher.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events", new DestinationEventRequest(EventType.DestinationArrival, null, "driver said so"));
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events", new DestinationEventRequest(EventType.DestinationArrival, null, " "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var done = await OpsScenario.Ok<LrDetailDto>(s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events", new DestinationEventRequest(EventType.DestinationArrival, null, "GPS failed at the gate; checked with the distributor")));

        done.Lr.Status.ShouldBe(DeliveryStatus.Arrived);
        var arrival = done.Events.Single(e => e.Type == EventType.DestinationArrival);
        (arrival.Source, arrival.Verification).ShouldBe((EventSource.Office, "OfficeOverride"));
        arrival.Remarks.ShouldNotBeNull().ShouldContain("GPS failed");
        (await s.ViewAsync(token)).NextAction.ShouldBe(QrActions.ConfirmUnloading);
    }
}

[Collection(ApiCollection.Name)]
public class TransportOpsPodApiTests(TmsApiFactory factory)
{
    private static async Task<(OpsScenario S, Guid LrId, string Token)> DeliveredAtGateAsync(TmsApiFactory factory)
    {
        var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();
        var trip = await s.CreateTripAsync(lr.Lr.Lr.Id);
        await s.DepartAsync(trip.Trip.Id);
        var token = OpsScenario.Token(lr);
        (await s.ArriveAtPuneAsync(token)).EnsureSuccessStatusCode();
        (await s.UnloadAsync(token)).EnsureSuccessStatusCode();
        return (s, lr.Lr.Lr.Id, token);
    }

    [Fact]
    public async Task The_driver_uploads_the_pod_straight_away_it_is_received_and_never_accepted_by_the_upload()
    {
        var (s, lrId, token) = await DeliveredAtGateAsync(factory);
        using var _s = s;

        var response = await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files("front", "back"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await response.ReadAsync<QrViewDto>();
        view.PodStatus.ShouldBe(PodStatus.Received);
        view.NextAction.ShouldBe(QrActions.None);
        var pod = await OpsScenario.Ok<PodDto>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lrId}/pod"));
        pod.Status.ShouldBe(PodStatus.Received);
        pod.Documents.Count.ShouldBe(2);
        pod.Documents.ShouldAllBe(d => d.Source == PodSource.QrWeb && d.Submission == 1);
        pod.ReviewerId.ShouldBeNull();
        (await s.GetLrAsync(lrId)).Events.ShouldContain(e => e.Type == EventType.PodUploaded && e.Source == EventSource.QrWeb);
        var file = await s.Admin.GetAsync($"/api/v1/ops/pod-documents/{pod.Documents[0].Id}/file");
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Anonymous.GetAsync($"/api/v1/ops/pod-documents/{pod.Documents[0].Id}/file")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // evidence is never public
    }

    [Fact]
    public async Task A_pod_upload_before_unloading_is_refused_and_so_are_bad_files()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();
        var trip = await s.CreateTripAsync(lr.Lr.Lr.Id);
        await s.DepartAsync(trip.Trip.Id);
        var token = OpsScenario.Token(lr);
        (await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files("early"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.ArriveAtPuneAsync(token)).EnsureSuccessStatusCode();
        (await s.UnloadAsync(token)).EnsureSuccessStatusCode();

        var text = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("hello")), "files", "pod.png" } };
        (await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), text)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), new MultipartFormDataContent())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files(Enumerable.Range(0, 11).Select(i => $"f{i}").ToArray()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files("same"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var duplicate = await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files("same"));
        (await OpsScenario.Ok<PodDto>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/pod"))).Documents.Count.ShouldBe(1);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_retried_upload_with_the_same_key_stores_the_files_once()
    {
        var (s, lrId, token) = await DeliveredAtGateAsync(factory);
        using var _s = s;
        var key = Guid.NewGuid().ToString("N");

        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, OpsScenario.Url(token, "/pod")) { Content = OpsScenario.Files("retry-a", "retry-b") };
            request.Headers.Add("Idempotency-Key", key);
            (await s.Anonymous.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await OpsScenario.Ok<PodDto>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lrId}/pod"))).Documents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_driver_can_say_the_pod_is_not_available_yet_and_the_delivery_stays_complete()
    {
        var (s, lrId, token) = await DeliveredAtGateAsync(factory);
        using var _s = s;

        var view = await OpsScenario.Ok<QrViewDto>(s.Anonymous.PostJsonAsync(OpsScenario.Url(token, "/pod-pending"), new PodPendingRequest(null)));

        view.Status.ShouldBe(DeliveryStatus.UnloadingCompleted);
        view.PodStatus.ShouldBe(PodStatus.Pending);
        (await s.GetLrAsync(lrId)).Events.ShouldContain(e => e.Type == EventType.PodPending);
        var queue = await OpsScenario.Ok<PagedResult<PodQueueItemDto>>(s.Admin.GetAsync($"/api/v1/ops/pods?view=pending&search={view.LrNumber}"));
        queue.Items.ShouldContain(i => i.LrId == lrId && i.Status == PodStatus.Pending);
    }

    [Fact]
    public async Task The_office_uploads_the_missing_pod_later_reviews_it_rejects_it_and_the_correction_keeps_the_earlier_files()
    {
        var (s, lrId, token) = await DeliveredAtGateAsync(factory);
        using var _s = s;
        await s.Anonymous.PostJsonAsync(OpsScenario.Url(token, "/pod-pending"), new PodPendingRequest(null));

        (await OpsScenario.Ok<PodDto>(s.Admin.PostAsync($"/api/v1/ops/lrs/{lrId}/pod", OpsScenario.Files("scan-1")))).Status.ShouldBe(PodStatus.Received);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/reject", new PodDecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rejected = await OpsScenario.Ok<PodDto>(s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/reject", new PodDecisionRequest("Signature is missing")));
        rejected.Status.ShouldBe(PodStatus.Rejected);
        rejected.RejectionReason.ShouldBe("Signature is missing");

        // The driver's page now asks for a corrected copy and says why.
        var view = await s.ViewAsync(token);
        (view.NextAction, view.PodRejectionReason).ShouldBe((QrActions.UploadPod, "Signature is missing"));
        var corrected = await OpsScenario.Ok<QrViewDto>(s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files("scan-2")));
        corrected.PodStatus.ShouldBe(PodStatus.Received);

        var pod = await OpsScenario.Ok<PodDto>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lrId}/pod"));
        pod.Documents.Select(d => (d.Submission, d.Source)).ShouldBe([(1, PodSource.Office), (2, PodSource.QrWeb)]);
        pod.Reviews.Select(r => r.Action).ShouldBe(["Reject"]);

        var accepted = await OpsScenario.Ok<PodDto>(s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/accept", new PodDecisionRequest("Clear copy")));
        accepted.Status.ShouldBe(PodStatus.Accepted);
        accepted.ReviewerId.ShouldNotBeNull();
        accepted.Reviews.Select(r => r.Action).ShouldBe(["Reject", "Accept"]);
        (await s.GetLrAsync(lrId)).Lr.Status.ShouldBe(DeliveryStatus.UnloadingCompleted); // the delivery times do not move

        // After acceptance the code still opens, but only to say it is complete.
        var done = await s.ViewAsync(token);
        (done.NextAction, done.PodStatus).ShouldBe((QrActions.None, PodStatus.Accepted));
        (await s.Anonymous.PostAsync(OpsScenario.Url(token, "/pod"), OpsScenario.Files("late"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostAsync($"/api/v1/ops/lrs/{lrId}/pod", OpsScenario.Files("late-office"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Accepting_needs_a_received_pod_and_the_review_permission()
    {
        var (s, lrId, _) = await DeliveredAtGateAsync(factory);
        using var _s = s;
        using var uploader = await factory.UserWithPermissionsAsync(s.Admin, OpsPermissions.Read, OpsPermissions.PodUpload);

        (await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/accept", new PodDecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // nothing received yet
        (await uploader.PostAsync($"/api/v1/ops/lrs/{lrId}/pod", OpsScenario.Files("by-office"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await uploader.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/accept", new PodDecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_office_cannot_upload_a_pod_before_the_delivery_is_complete()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();

        var response = await s.Admin.PostAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/pod", OpsScenario.Files("too-early"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("ops.pod_state");
    }
}
