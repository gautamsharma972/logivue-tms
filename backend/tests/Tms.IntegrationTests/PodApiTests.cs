using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.DeliveryScenario;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PodApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_clean_delivery_with_its_photo_is_accepted_by_itself_and_the_delivery_closes()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());
        pod.Summary.Status.ShouldBe(PodStatus.Draft);
        pod.Missing.ShouldContain(m => m.Contains("photo"));

        var done = await s.PhotoAndSubmitAsync(pod);

        done.Summary.Status.ShouldBe(PodStatus.Accepted);
        done.AutoAccepted.ShouldBeTrue();
        done.Validations.ShouldAllBe(v => v.Status == ValidationOutcome.Valid);
        done.Reviews.Select(r => r.Action).ShouldBe(["AutoAccepted"]);
        var delivery = await (await s.Vendor.GetAsync($"/api/v1/deliveries/{done.Summary.DeliveryId}")).ReadAsync<DeliveryDto>();
        delivery.Summary.Status.ShouldBe(DeliveryStatus.Closed);
        delivery.Summary.PodStatus.ShouldBe(PodStatus.Accepted);
    }

    [Fact]
    public async Task A_proof_cannot_be_submitted_until_the_evidence_the_rules_ask_for_is_there()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());

        var early = await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);

        early.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await early.ProblemCodeAsync()).ShouldBe("pods.incomplete");
        (await early.Content.ReadAsStringAsync()).ShouldContain("photo");
        (await s.PodAsync(pod.Summary.Id)).Summary.Status.ShouldBe(PodStatus.Draft);
    }

    [Fact]
    public async Task Photos_are_checked_for_type_size_and_duplicates_and_a_retry_with_the_same_key_adds_nothing()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());
        var id = pod.Summary.Id;

        (await UploadAsync(s.Vendor, id, "not an image"u8.ToArray())).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // not a picture, whatever it is called
        (await UploadAsync(s.Vendor, id, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8])).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // a JPEG header with no image in it
        var tiny = await UploadAsync(s.Vendor, id, TestImages.Jpeg(100, 80, 5));
        tiny.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await tiny.ReadAsync<EvidenceDto>()).Warnings.ShouldNotBeNull().ShouldContain("below"); // small but kept, with a warning for the reviewer

        var first = await UploadAsync(s.Vendor, id, TestImages.Jpeg(seed: 1), idempotencyKey: "photo-1");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var retry = await UploadAsync(s.Vendor, id, TestImages.Jpeg(seed: 1), idempotencyKey: "photo-1");
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await retry.ReadAsync<EvidenceDto>()).Id.ShouldBe((await first.ReadAsync<EvidenceDto>()).Id);
        (await UploadAsync(s.Vendor, id, TestImages.Jpeg(seed: 1))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // the same file again without a key
        (await UploadAsync(s.Vendor, id, TestImages.Jpeg(seed: 1), EvidenceType.PodDocument, contentType: "application/pdf")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await s.PodAsync(id)).Evidence.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_blank_signature_is_refused_and_a_drawn_one_is_stored_and_counts_as_proof()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.PodRules)!;
        await s.SetSettingAsync(DeliverySettingKeys.PodRules, new PodRulesSetting(true, false, true, true, 1, false, true, false, 100, 30, 5));
        try
        {
            var pod = await s.CompletedAsync(await s.ArrivedAsync(), proof: new ProofRequest(ProofMethod.Signature, "Anil Kumar", "Manager", null, null, false, false));
            pod.Missing.ShouldContain(m => m.Contains("signature"));

            var blank = await SignAsync(s.Vendor, pod.Summary.Id, TestImages.Signature(drawn: false));
            blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await blank.ProblemCodeAsync()).ShouldBe("pods.signature_blank");
            (await SignAsync(s.Vendor, pod.Summary.Id, "not a png"u8.ToArray())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var signed = await SignAsync(s.Vendor, pod.Summary.Id, TestImages.Signature(drawn: true));
            signed.StatusCode.ShouldBe(HttpStatusCode.Created, await signed.Content.ReadAsStringAsync());
            var signature = await signed.ReadAsync<SignatureDto>();
            signature.SignerName.ShouldBe("Anil Kumar");

            var again = await SignAsync(s.Vendor, pod.Summary.Id, TestImages.Signature(drawn: true)); // a retry of the same signature
            (await again.ReadAsync<SignatureDto>()).Id.ShouldBe(signature.Id);

            var done = await s.PhotoAndSubmitAsync(await s.PodAsync(pod.Summary.Id));
            done.Signatures.Count.ShouldBe(1);
            done.Summary.Status.ShouldBe(PodStatus.Accepted);

            var file = await s.Vendor.GetAsync($"/api/v1/pod-signatures/{signature.Id}/file");
            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            file.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            (await s.Rival.GetAsync($"/api/v1/pod-signatures/{signature.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.PodRules, original);
        }
    }

    [Fact]
    public async Task A_contactless_delivery_needs_the_drivers_confirmation_a_photo_and_the_location()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync(), proof: new ProofRequest(ProofMethod.Contactless, "Gate security", null, null, null, false, false));
        pod.Missing.ShouldContain(m => m.Contains("contactless"));

        (await s.Vendor.PutJsonAsync($"/api/v1/pods/{pod.Summary.Id}/proof", new UpdateProofRequest(new ProofRequest(ProofMethod.Contactless, "Gate security", null, null, "Left at the gate", true, false)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var done = await s.PhotoAndSubmitAsync(await s.PodAsync(pod.Summary.Id));

        done.Method.ShouldBe(ProofMethod.Contactless);
        done.Summary.Status.ShouldBe(PodStatus.Accepted);
    }

    [Fact]
    public async Task A_shortage_and_damage_need_a_damage_photo_then_go_to_a_reviewer_who_accepts_and_the_exceptions_stay_open()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var item = arrived.Items[0];
        var pod = await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(item, 95, @short: 3) with { DamagedQuantity = 2, DamageType = "BROKEN", DamageReason = "Crushed" });
        pod.Missing.ShouldContain("A photo of the damage");

        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: 11))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // still no damage photo
        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: 12), EvidenceType.DamagePhoto)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var submitted = await (await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null)).ReadAsync<PodDto>();

        submitted.Summary.Status.ShouldBe(PodStatus.UnderReview); // a discrepancy is never accepted without a person
        submitted.Validations.ShouldContain(v => v.Check == "Discrepancy" && v.Status == ValidationOutcome.RequiresReview);

        (await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // a vendor never approves its own paperwork
        var queue = await (await s.Admin.GetAsync("/api/v1/pods/review-queue?pageSize=100")).ReadAsync<PagedResult<PodSummaryDto>>();
        queue.Items.ShouldContain(p => p.Id == pod.Summary.Id);

        var accepted = await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/approve", null);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        (await accepted.ReadAsync<PodDto>()).Summary.Status.ShouldBe(PodStatus.Accepted);

        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={pod.Summary.DeliveryId}&openOnly=true")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.Select(e => e.Type).ShouldBe([ExceptionType.Shortage, ExceptionType.Damage], ignoreOrder: true); // acceptance does not make them go away
    }

    [Fact]
    public async Task A_rejected_proof_needs_a_reason_raises_an_exception_and_can_be_resubmitted()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var pod = await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(arrived.Items[0], 97, @short: 3));
        var submitted = await s.PhotoAndSubmitAsync(pod, 21);
        submitted.Summary.Status.ShouldBe(PodStatus.UnderReview);

        (await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/reject", new ReasonRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rejected = await (await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/reject", new ReasonRequest("The photo is of the wrong site"))).ReadAsync<PodDto>();
        rejected.Summary.Status.ShouldBe(PodStatus.Rejected);
        rejected.RejectionReason.ShouldBe("The photo is of the wrong site");

        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={pod.Summary.DeliveryId}")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.ShouldContain(e => e.Type == ExceptionType.PodRejected);

        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: 22))).StatusCode.ShouldBe(HttpStatusCode.Created); // a better photo
        var again = await (await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/resubmit", null)).ReadAsync<PodDto>();
        again.Summary.Status.ShouldBe(PodStatus.UnderReview);
        again.RejectionCount.ShouldBe(1);
        again.Reviews.Select(r => r.Action).ShouldBe(["Rejected", "Resubmitted"]);
    }

    [Fact]
    public async Task Asking_for_more_evidence_returns_the_proof_without_rejecting_it()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var pod = await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(arrived.Items[0], 97, @short: 3));
        await s.PhotoAndSubmitAsync(pod, 31);

        var back = await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/review", new ReviewPodRequest("resubmission", "Add the unloading bay"));
        (await back.ReadAsync<PodDto>()).Summary.Status.ShouldBe(PodStatus.ResubmissionRequired);
        (await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/review", new ReviewPodRequest("dance", null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_accepted_proof_is_corrected_as_a_new_version_and_the_old_one_stays_untouched()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.PhotoAndSubmitAsync(await s.CompletedAsync(await s.ArrivedAsync()), 41);
        pod.Summary.Status.ShouldBe(PodStatus.Accepted);

        (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: 42))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // locked once accepted
        (await s.Vendor.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/correction", new RequestCorrectionRequest("misspelt"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var next = await (await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/correction", new RequestCorrectionRequest("The recipient's name was misspelt"))).ReadAsync<PodDto>();
        next.Summary.Version.ShouldBe(2);
        next.Summary.PodNumber.ShouldBe(pod.Summary.PodNumber);
        next.Summary.Status.ShouldBe(PodStatus.Captured); // the copied evidence already satisfies the rules
        next.Evidence.Count.ShouldBe(pod.Evidence.Count); // the original evidence is carried over, not moved

        var old = await s.PodAsync(pod.Summary.Id);
        old.Summary.Status.ShouldBe(PodStatus.Accepted);
        old.Summary.IsCurrent.ShouldBeFalse();
        old.RecipientName.ShouldBe("Anil Kumar");

        var all = await (await s.Admin.GetAsync($"/api/v1/pods?search={pod.Summary.PodNumber}&currentOnly=false")).ReadAsync<PagedResult<PodSummaryDto>>();
        all.Items.Select(p => p.Version).Order().ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Evidence_files_are_only_available_to_those_allowed_to_see_the_proof()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());
        var evidence = await (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: 51))).ReadAsync<EvidenceDto>();
        var url = $"/api/v1/pod-evidence/{evidence.Id}/file";

        (await s.Vendor.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Admin.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Rival.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Rival.GetAsync($"/api/v1/pods/{pod.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Rival.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Removing_evidence_keeps_the_record_and_needs_a_reason()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());
        var evidence = await (await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: 61))).ReadAsync<EvidenceDto>();

        (await s.Vendor.DeleteAsync($"/api/v1/pods/{pod.Summary.Id}/evidence/{evidence.Id}?reason=")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var removed = await (await s.Vendor.DeleteAsync($"/api/v1/pods/{pod.Summary.Id}/evidence/{evidence.Id}?reason=Blurred")).ReadAsync<PodDto>();

        removed.Evidence.Single().Removed.ShouldBeTrue();
        removed.Evidence.Single().RemovedReason.ShouldBe("Blurred");
        removed.Missing.ShouldContain(m => m.Contains("photo"));
    }

    [Fact]
    public async Task A_delivery_outside_its_geofence_is_flagged_not_rejected()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        // The customer is in Pune; the driver reports Here() ~ Pune too, so move the customer far away.
        var arrived = await s.ArrivedAsync(s.Request(lat: 19.0760, lon: 72.8777, radius: 300)); // Mumbai
        var pod = await s.CompletedAsync(arrived);
        pod.Geofence.ShouldBe(GeofenceStatus.Outside);

        var done = await s.PhotoAndSubmitAsync(pod, 71);

        done.Summary.Status.ShouldBe(PodStatus.UnderReview);
        done.Validations.Single(v => v.Check == "Geofence").Status.ShouldBe(ValidationOutcome.RequiresReview);
        done.Validations.Single(v => v.Check == "Geofence").Message.ShouldContain("m from the customer");
        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={done.Summary.DeliveryId}")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.ShouldContain(e => e.Type == ExceptionType.GpsException);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid podId, byte[] bytes, EvidenceType type = EvidenceType.PackagePhoto, string? idempotencyKey = null, string contentType = "image/jpeg") =>
        DeliveryScenario.UploadAsync(client, podId, bytes, type, idempotencyKey, contentType);
}
