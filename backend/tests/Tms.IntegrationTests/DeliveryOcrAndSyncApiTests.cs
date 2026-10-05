using System.Net;
using System.Text.Json;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Mobile;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.DeliveryScenario;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeliveryOcrAndSyncApiTests(TmsApiFactory factory)
{
    private static string Lines(SaveDeliveryRequest d, string quantity, string confidence = "98") =>
        string.Join('\n', $"Shipment Number: {d.ShipmentReference} [{confidence}%]", "Vehicle Number: MH12AB1234 [96%]", $"Delivered Quantity: {quantity} [{confidence}%]", "Recipient Name: Anil Kumar [92%]");

    private async Task<(DeliveryScenario Scenario, SaveDeliveryRequest Request, PodDto Pod)> PodWithPaperAsync(Func<SaveDeliveryRequest, byte[]> paper, DeliveryOutcome outcome = DeliveryOutcome.Full, int shortage = 0)
    {
        var s = await DeliveryScenario.CreateAsync(factory);
        var request = s.Request();
        var arrived = await s.ArrivedAsync(request);
        var item = arrived.Items[0];
        var pod = await s.CompletedAsync(arrived, outcome, null, Qty(item, 100 - shortage, @short: shortage));
        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: Random.Shared.Next()))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await UploadAsync(s.Vendor, pod.Summary.Id, paper(request), EvidenceType.PodDocument, contentType: "application/pdf")).StatusCode.ShouldBe(HttpStatusCode.Created);
        return (s, request, pod);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid podId, byte[] bytes, EvidenceType type = EvidenceType.PackagePhoto, string contentType = "image/jpeg") =>
        DeliveryScenario.UploadAsync(client, podId, bytes, type, null, contentType);

    [Fact]
    public async Task A_paper_pod_that_matches_the_system_is_read_in_the_background_and_the_proof_is_accepted()
    {
        var (s, _, pod) = await PodWithPaperAsync(r => TestImages.PaperPod(Lines(r, "100").Split('\n')));
        using var _s = s;

        var submitted = await (await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null)).ReadAsync<PodDto>();
        submitted.Summary.Status.ShouldBeOneOf(PodStatus.Submitted, PodStatus.Accepted); // returns without waiting for the reading

        var done = await s.WaitForAsync(pod.Summary.Id, p => p.Summary.Status == PodStatus.Accepted);
        done.Ocr.Single().Status.ShouldBe(OcrStatus.Completed);
        done.Ocr.Single().Provider.ShouldBe("text-layer");
        done.Ocr.Single().Fields.ShouldAllBe(f => f.Status == OcrFieldStatus.Matched);
        done.Validations.ShouldContain(v => v.Type == "Ocr" && v.Status == ValidationOutcome.Valid);
    }

    [Fact]
    public async Task A_low_confidence_reading_sends_the_proof_to_review_with_the_field_marked()
    {
        var (s, _, pod) = await PodWithPaperAsync(r => TestImages.PaperPod(Lines(r, "100", "64").Split('\n')));
        using var _s = s;

        await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);
        var done = await s.WaitForAsync(pod.Summary.Id, p => p.Summary.Status == PodStatus.UnderReview);

        done.Ocr.Single().Fields.Single(f => f.Name == "Shipment Number").Status.ShouldBe(OcrFieldStatus.LowConfidence);
        done.Ocr.Single().Fields.Single(f => f.Name == "Shipment Number").Threshold.ShouldBe(0.95m);
        done.Validations.ShouldContain(v => v.Type == "Ocr" && v.Status == ValidationOutcome.RequiresReview);
    }

    [Fact]
    public async Task The_demo_case_95_delivered_3_short_2_damaged_with_a_doubtful_quantity_is_reviewed_corrected_and_accepted()
    {
        var s = await DeliveryScenario.CreateAsync(factory);
        using var _s = s;
        var request = s.Request();
        var arrived = await s.ArrivedAsync(request);
        var pod = await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(arrived.Items[0], 95, @short: 3, damaged: 2));
        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: Random.Shared.Next()))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: Random.Shared.Next()), EvidenceType.DamagePhoto)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var paper = TestImages.PaperPod($"Shipment Number: {request.ShipmentReference} [98%]", "Vehicle Number: MH12AB1234 [96%]", "Delivered Quantity: 92 [64%]", "Recipient Name: Anil Kumar [90%]");
        (await UploadAsync(s.Vendor, pod.Summary.Id, paper, EvidenceType.PodDocument, "application/pdf")).StatusCode.ShouldBe(HttpStatusCode.Created);
        await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);

        var review = await s.WaitForAsync(pod.Summary.Id, p => p.Summary.Status == PodStatus.UnderReview);
        var workbench = await (await s.Admin.GetAsync($"/api/v1/pods/{pod.Summary.Id}/review")).ReadAsync<PodReviewDto>();
        workbench.DocumentEvidenceId.ShouldNotBeNull();
        var quantity = workbench.Ocr!.Fields.Single(f => f.Name == "Delivered Quantity");
        (quantity.Status, quantity.Confidence, quantity.Expected).ShouldBe((OcrFieldStatus.LowConfidence, 0.64m, "95"));
        workbench.Ocr.Fields.Single(f => f.Name == "Shipment Number").Status.ShouldBe(OcrFieldStatus.Matched);
        review.Validations.Single(v => v.Check == "Discrepancy").Status.ShouldBe(ValidationOutcome.RequiresReview);

        // Only a reviewer may edit, with a reason; the original reading is kept beside the correction.
        (await s.Vendor.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/ocr/review", new ReviewOcrFieldRequest("Delivered Quantity", "95", "misread"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/ocr/review", new ReviewOcrFieldRequest("Delivered Quantity", "95", " "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var edited = await (await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/ocr/review", new ReviewOcrFieldRequest("Delivered Quantity", "95", "Paper 5 read as 2 by the scanner"))).ReadAsync<PodDto>();
        var field = edited.Ocr.Single().Fields.Single(f => f.Name == "Delivered Quantity");
        (field.RawValue, field.ReviewedValue, field.EffectiveValue, field.Status).ShouldBe(("92", "95", "95", OcrFieldStatus.Matched));
        edited.Reviews.Single(r => r.Action == "FieldEdited").NewValue.ShouldBe("95");
        edited.Reviews.Single(r => r.Action == "FieldEdited").OldValue.ShouldBe("92");

        var accepted = await (await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/approve", null)).ReadAsync<PodDto>();
        accepted.Summary.Status.ShouldBe(PodStatus.Accepted);

        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={pod.Summary.DeliveryId}&openOnly=true")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.Select(e => e.Type).ShouldContain(ExceptionType.Shortage);
        exceptions.Items.Select(e => e.Type).ShouldContain(ExceptionType.Damage);
    }

    [Fact]
    public async Task A_paper_that_disagrees_with_the_system_shows_both_values_and_raises_an_exception()
    {
        var (s, _, pod) = await PodWithPaperAsync(r => TestImages.PaperPod($"Shipment Number: SH-OTHER-1 [99%]", "Vehicle Number: MH99ZZ0000 [99%]", "Delivered Quantity: 100 [99%]"));
        using var _s = s;

        await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);
        var done = await s.WaitForAsync(pod.Summary.Id, p => p.Summary.Status == PodStatus.UnderReview);

        var shipment = done.Ocr.Single().Fields.Single(f => f.Name == "Shipment Number");
        shipment.Status.ShouldBe(OcrFieldStatus.Mismatch);
        shipment.Message.ShouldNotBeNull().ShouldContain("SH-OTHER-1");
        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={pod.Summary.DeliveryId}")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.ShouldContain(e => e.Type == ExceptionType.OcrValidationFailed);
    }

    [Fact]
    public async Task A_document_that_cannot_be_read_is_a_failure_for_a_person_not_a_guess()
    {
        var (s, _, pod) = await PodWithPaperAsync(_ => TestImages.PaperPod("Nothing useful here"));
        using var _s = s;

        await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);
        var done = await s.WaitForAsync(pod.Summary.Id, p => p.Summary.Status == PodStatus.UnderReview);

        done.Ocr.Single().Status.ShouldBe(OcrStatus.Failed);
        done.Ocr.Single().Error.ShouldNotBeNull();
        done.Ocr.Single().Fields.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reading_can_be_asked_for_again_and_a_paper_must_exist_first()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());
        (await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/ocr", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.PaperPod("Shipment Number: X [99%]"), EvidenceType.PodDocument, "application/pdf")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var queued = await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/ocr", null);
        queued.StatusCode.ShouldBe(HttpStatusCode.OK, await queued.Content.ReadAsStringAsync());
        await s.WaitForAsync(pod.Summary.Id, p => p.Ocr.Count > 0 && p.Ocr.All(o => o.Status is OcrStatus.Completed or OcrStatus.Failed));
        (await s.Vendor.GetAsync($"/api/v1/pods/{pod.Summary.Id}/ocr")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- offline synchronisation

    private static MobileSyncRequest Sync(string device, params SyncCommand[] commands) => new(device, commands);

    private static SyncCommand Command(string key, string type, Guid delivery, object? payload = null) =>
        new(key, type, delivery, DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow.AddMinutes(-20), payload is null ? null : JsonSerializer.SerializeToElement(payload, ApiExtensions.Json));

    private static async Task<MobileSyncResponse> PostAsync(HttpClient client, MobileSyncRequest request)
    {
        var response = await client.PostJsonAsync("/api/v1/mobile/sync", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MobileSyncResponse>();
    }

    [Fact]
    public async Task A_device_downloads_its_deliveries_with_the_rules_it_needs_to_work_offline()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();

        var bundle = await (await s.Vendor.GetAsync("/api/v1/mobile/deliveries")).ReadAsync<MobileBundleDto>();

        bundle.Deliveries.ShouldContain(d => d.Delivery.Summary.Id == delivery.Summary.Id);
        bundle.Config.AttemptReasons.ShouldNotBeEmpty();
        bundle.Config.DamageTypes.ShouldContain(t => t.Code == "BROKEN" && t.EvidenceRequired);
        bundle.Config.Pod.GpsRequired.ShouldBeTrue();
        var theirs = await (await s.Rival.GetAsync("/api/v1/mobile/deliveries")).ReadAsync<MobileBundleDto>();
        theirs.Deliveries.ShouldNotContain(d => d.Delivery.Summary.Id == delivery.Summary.Id);
    }

    [Fact]
    public async Task Work_done_offline_is_applied_in_order_and_a_retry_changes_nothing()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();
        var id = delivery.Summary.Id;
        var key = Guid.NewGuid().ToString("N");
        var complete = Complete(DeliveryOutcome.Full, null, null, Qty(delivery.Items[0], 100));
        var request = Sync("device-9",
            Command($"{key}-1", "start", id, Here()),
            Command($"{key}-2", "arrive", id, Here()),
            Command($"{key}-3", "complete", id, complete));

        var first = await PostAsync(s.Vendor, request);
        first.Results.Select(r => (r.Status, r.ErrorCode, r.Error)).ShouldBe([(SyncStatus.Synced, null, null), (SyncStatus.Synced, null, null), (SyncStatus.Synced, null, null)]);
        first.Results[2].DeliveryStatus.ShouldBe(DeliveryStatus.Delivered);
        var podId = first.Results[2].PodId.ShouldNotBeNull();

        // The phone never saw the answer, so it sends everything again.
        var retry = await PostAsync(s.Vendor, request);
        retry.Results.ShouldAllBe(r => r.Status == SyncStatus.Synced && r.Duplicate);

        var after = await (await s.Admin.GetAsync($"/api/v1/deliveries/{id}")).ReadAsync<DeliveryDto>();
        after.Events.Count(e => e.Type == DeliveryEventType.Delivered).ShouldBe(1);
        after.Attempts.Count.ShouldBe(1);
        var pods = await (await s.Admin.GetAsync($"/api/v1/pods?search={delivery.Summary.Number}&currentOnly=false")).ReadAsync<PagedResult<PodSummaryDto>>();
        pods.Items.Count.ShouldBe(1);
        pods.Items[0].Id.ShouldBe(podId);

        // The times on the timeline are the device's own, not the moment the signal came back.
        after.Events.Single(e => e.Type == DeliveryEventType.Started).At.ShouldBeLessThan(DateTimeOffset.UtcNow.AddMinutes(-15));
        after.Events.Single(e => e.Type == DeliveryEventType.Started).DeviceReference.ShouldBe("device-1");
    }

    [Fact]
    public async Task A_command_that_no_longer_fits_is_a_conflict_a_bad_one_fails_and_neither_stops_the_rest()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var one = await s.DeliveryAsync();
        var two = await s.DeliveryAsync();
        var key = Guid.NewGuid().ToString("N");

        // Another device already started delivery one.
        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{one.Summary.Id}/start", Here())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await PostAsync(s.Vendor, Sync("device-2",
            Command($"{key}-a", "start", one.Summary.Id, Here()), // conflict
            Command($"{key}-b", "attempt", two.Summary.Id, new AttemptRequest("CUSTOMER_UNAVAILABLE", null, null, null, Here())), // not arrived yet
            Command($"{key}-c", "start", two.Summary.Id, Here()), // fine
            Command($"{key}-d", "complete", two.Summary.Id, new { nonsense = true }))); // unreadable

        result.Results.Select(r => r.Status).ShouldBe([SyncStatus.Conflict, SyncStatus.Conflict, SyncStatus.Synced, SyncStatus.Failed]);
        result.Results[0].ErrorCode.ShouldBe("deliveries.invalid_state");
        result.Results[3].ErrorCode.ShouldBe("mobile.payload_invalid");
        (await s.Admin.GetAsync($"/api/v1/deliveries/{two.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await s.Admin.GetAsync($"/api/v1/deliveries/{two.Summary.Id}")).ReadAsync<DeliveryDto>()).Summary.Status.ShouldBe(DeliveryStatus.EnRoute);

        // A command that failed is tried again with the same key and can succeed once the cause is gone.
        var again = await PostAsync(s.Vendor, Sync("device-2", Command($"{key}-b", "attempt", two.Summary.Id, new AttemptRequest("CUSTOMER_UNAVAILABLE", null, null, null, Here()))));
        again.Results[0].Status.ShouldBe(SyncStatus.Conflict);
        again.Results[0].Attempt.ShouldBe(2);
        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{two.Summary.Id}/arrive", Here())).StatusCode.ShouldBe(HttpStatusCode.OK);
        var fixedIt = await PostAsync(s.Vendor, Sync("device-2", Command($"{key}-b", "attempt", two.Summary.Id, new AttemptRequest("CUSTOMER_UNAVAILABLE", null, null, null, Here()))));
        fixedIt.Results[0].Status.ShouldBe(SyncStatus.Synced);
        fixedIt.Results[0].Attempt.ShouldBe(3);
    }

    [Fact]
    public async Task A_device_cannot_sync_work_on_another_companys_delivery_or_send_an_unknown_command()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();

        var rival = await PostAsync(s.Rival, Sync("device-3", Command(Guid.NewGuid().ToString("N"), "start", delivery.Summary.Id, Here())));
        rival.Results[0].Status.ShouldBe(SyncStatus.Failed);
        rival.Results[0].ErrorCode.ShouldBe("deliveries.not_found");

        var bad = await s.Vendor.PostJsonAsync("/api/v1/mobile/sync", Sync("device-3", Command("k1", "teleport", delivery.Summary.Id)));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Vendor.PostJsonAsync("/api/v1/mobile/sync", Sync("device-3"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var staffWithoutExecute = await factory.UserWithPermissionsAsync(s.Admin, DeliveryPermissions.Read);
        (await staffWithoutExecute.PostJsonAsync("/api/v1/mobile/sync", Sync("d", Command("k2", "start", delivery.Summary.Id)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
