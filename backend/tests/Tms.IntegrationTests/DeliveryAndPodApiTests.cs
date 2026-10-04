using System.Net;
using System.Net.Http.Headers;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.Delivery;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeliveryAndPodApiTests(TmsApiFactory factory)
{
    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    /// <summary>A shipment with the given orders, accepted by the vendor and dispatched.</summary>
    private static async Task<(ShipmentDto Shipment, IReadOnlyList<OrderDto> Orders)> DispatchedAsync(ShipmentScenario s, params decimal[] weights)
    {
        var orders = new List<OrderDto>();
        foreach (var w in weights)
        {
            orders.Add(await s.OrderAsync(w));
        }

        var tendered = await s.TenderedAsync([.. orders]);
        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{tendered.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var dispatched = await s.Admin.PostAsync($"/api/v1/shipments/{tendered.Summary.Id}/dispatch", null);
        dispatched.StatusCode.ShouldBe(HttpStatusCode.OK, await dispatched.Content.ReadAsStringAsync());
        return (await dispatched.ReadAsync<ShipmentDto>(), orders);
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid shipmentId, Guid orderId, byte[] content, string name = "pod.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf"); // deliberately not trusted by the server
        form.Add(file, "file", name);
        return client.PostAsync($"/api/v1/shipments/{shipmentId}/orders/{orderId}/pod", form);
    }

    private static string Url(ShipmentDto s, Guid orderId, string tail) => $"/api/v1/shipments/{s.Summary.Id}/orders/{orderId}/{tail}";

    [Fact]
    public async Task A_vendor_confirms_each_delivery_and_the_shipment_completes_with_its_last_order()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m, 4_000m);

        shipment.Orders.ShouldAllBe(o => o.PackagesShipped == 40 && o.DeliveredAt == null); // what to count against is known before the delivery

        var first = await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store manager", null, null, null));
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var afterFirst = await first.ReadAsync<ShipmentDto>();
        afterFirst.Summary.Status.ShouldBe(ShipmentStatus.Dispatched);
        afterFirst.Orders.Single(o => o.OrderId == orders[0].Id).ReceiverName.ShouldBe("Store manager");

        var again = await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store manager", null, null, null));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("delivery.already_recorded");

        var last = await (await s.Vendor.PostJsonAsync(Url(shipment, orders[1].Id, "delivery"), new RecordDeliveryRequest(null, "Dock supervisor", null, null, null))).ReadAsync<ShipmentDto>();
        last.Summary.Status.ShouldBe(ShipmentStatus.Delivered);
        last.DeliveredAt.ShouldNotBeNull();
        (await (await s.Admin.GetAsync($"/api/v1/orders/{orders[0].Id}")).ReadAsync<OrderDto>()).Status.ShouldBe(OrderStatus.Delivered);
    }

    [Fact]
    public async Task Shortage_and_damage_are_recorded_validated_and_visible_to_staff()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m);

        var missingReason = await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store", 8, 1, null));
        missingReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store", 8, 1, "Two cartons missing, one wet"));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());

        var line = (await (await s.Admin.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).ReadAsync<ShipmentDto>()).Orders.Single();
        (line.PackagesShipped, line.DeliveredPackages, line.DamagedPackages, line.ShortagePackages).ShouldBe((40, 8, 1, 32)); // the scenario's order has 40 packages
        line.DeliveryRemarks.ShouldBe("Two cartons missing, one wet");

        var queue = await (await s.Admin.GetAsync($"/api/v1/pod?stage=AwaitingProof&search={shipment.Summary.Number}")).ReadAsync<PagedResult<PodLineDto>>();
        queue.Items.ShouldHaveSingleItem().HasException.ShouldBeTrue();
    }

    [Fact]
    public async Task Proof_is_checked_by_content_attached_after_delivery_and_limited_in_number()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m);
        var orderId = orders[0].Id;

        (await Upload(s.Vendor, shipment.Summary.Id, orderId, Pdf)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // not delivered yet
        await s.Vendor.PostJsonAsync(Url(shipment, orderId, "delivery"), new RecordDeliveryRequest(null, "Store", null, null, null));

        var disguised = await Upload(s.Vendor, shipment.Summary.Id, orderId, System.Text.Encoding.ASCII.GetBytes("<script>alert(1)</script>"), "pod.pdf");
        disguised.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await disguised.ProblemCodeAsync()).ShouldBe("pod.file_type");

        var good = await Upload(s.Vendor, shipment.Summary.Id, orderId, Pdf);
        good.StatusCode.ShouldBe(HttpStatusCode.Created, await good.Content.ReadAsStringAsync());
        var doc = await good.ReadAsync<PodDocumentDto>();
        doc.ContentType.ShouldBe("application/pdf");

        var download = await s.Admin.GetAsync($"/api/v1/pod-documents/{doc.Id}/file");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);

        for (var i = 0; i < PodDocument.MaxPerOrder - 1; i++)
        {
            (await Upload(s.Vendor, shipment.Summary.Id, orderId, Png, "photo.png")).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        (await Upload(s.Vendor, shipment.Summary.Id, orderId, Png, "photo.png")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await (await s.Admin.GetAsync(Url(shipment, orderId, "pod"))).ReadAsync<List<PodDocumentDto>>()).Count.ShouldBe(PodDocument.MaxPerOrder);
    }

    [Fact]
    public async Task Staff_verify_or_reject_proof_and_only_those_with_the_permission()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m);
        var orderId = orders[0].Id;
        await s.Vendor.PostJsonAsync(Url(shipment, orderId, "delivery"), new RecordDeliveryRequest(null, "Store", null, null, null));
        var doc = await (await Upload(s.Vendor, shipment.Summary.Id, orderId, Pdf)).ReadAsync<PodDocumentDto>();
        using var planner = await factory.UserWithPermissionsAsync(s.Admin, "shipments.plan", "shipments.read");
        using var checker = await factory.UserWithPermissionsAsync(s.Admin, "shipments.read", "shipments.pod.verify");

        (await s.Vendor.PostAsync(Url(shipment, orderId, "pod/verify"), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // not on their own paperwork
        (await planner.PostAsync(Url(shipment, orderId, "pod/verify"), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var rejected = await checker.PostJsonAsync(Url(shipment, orderId, "pod/reject"), new ReasonRequest("Signature not legible"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        (await rejected.ReadAsync<ShipmentDto>()).Orders.Single().PodStatus.ShouldBe(PodStatus.Rejected);
        (await checker.PostJsonAsync(Url(shipment, orderId, "pod/reject"), new ReasonRequest(""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The vendor sees why, uploads a better copy, and it goes back to review.
        (await (await s.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).ReadAsync<ShipmentDto>()).Orders.Single().PodRejectionReason.ShouldBe("Signature not legible");
        (await Upload(s.Vendor, shipment.Summary.Id, orderId, Png, "clear.png")).StatusCode.ShouldBe(HttpStatusCode.Created);
        var verified = await checker.PostAsync(Url(shipment, orderId, "pod/verify"), null);
        verified.StatusCode.ShouldBe(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        (await verified.ReadAsync<ShipmentDto>()).Orders.Single().PodStatus.ShouldBe(PodStatus.Verified);

        // Verified proof is final.
        (await s.Vendor.DeleteAsync($"/api/v1/pod-documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Upload(s.Vendor, shipment.Summary.Id, orderId, Pdf)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_vendor_can_remove_a_wrong_upload_before_it_is_verified()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m);
        await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store", null, null, null));
        var doc = await (await Upload(s.Vendor, shipment.Summary.Id, orders[0].Id, Pdf)).ReadAsync<PodDocumentDto>();

        (await s.Vendor.DeleteAsync($"/api/v1/pod-documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await s.Admin.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).ReadAsync<ShipmentDto>()).Orders.Single().PodStatus.ShouldBe(PodStatus.Awaiting);
        (await s.Admin.GetAsync($"/api/v1/pod-documents/{doc.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_vendor_staff_without_access_and_other_tenants_see_none_of_it()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m);
        await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store", null, null, null));
        var doc = await (await Upload(s.Vendor, shipment.Summary.Id, orders[0].Id, Pdf)).ReadAsync<PodDocumentDto>();
        using var rival = await ShipmentScenario.CreateAsync(factory);
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        using var nobody = await factory.UserWithPermissionsAsync(s.Admin, "users.read");

        (await rival.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "x", null, null, null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Upload(rival.Vendor, shipment.Summary.Id, orders[0].Id, Pdf)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await rival.Vendor.GetAsync($"/api/v1/pod-documents/{doc.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await rival.Vendor.DeleteAsync($"/api/v1/pod-documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.GetAsync($"/api/v1/pod-documents/{doc.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await nobody.GetAsync("/api/v1/pod")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var own = await (await s.Vendor.GetAsync("/api/v1/pod?pageSize=100")).ReadAsync<PagedResult<PodLineDto>>();
        own.Items.ShouldAllBe(l => l.TransporterId == s.Transporter.Id);
        (await (await rival.Vendor.GetAsync("/api/v1/pod?pageSize=100")).ReadAsync<PagedResult<PodLineDto>>()).Items.ShouldNotContain(l => l.ShipmentId == shipment.Summary.Id);
        (await rival.Vendor.GetAsync("/api/v1/pod/ageing")).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // ageing is a staff report
    }

    [Fact]
    public async Task The_worklist_follows_each_delivery_through_its_stages_and_ageing_counts_outstanding_proof()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m, 2_000m);

        async Task<PodStage> StageOf(Guid orderId) =>
            (await (await s.Admin.GetAsync($"/api/v1/pod?search={shipment.Summary.Number}&pageSize=50")).ReadAsync<PagedResult<PodLineDto>>()).Items.Single(l => l.OrderId == orderId).Stage;

        (await StageOf(orders[0].Id)).ShouldBe(PodStage.DeliveryPending);
        await s.Vendor.PostJsonAsync(Url(shipment, orders[0].Id, "delivery"), new RecordDeliveryRequest(null, "Store", null, null, null));
        (await StageOf(orders[0].Id)).ShouldBe(PodStage.AwaitingProof);
        await Upload(s.Vendor, shipment.Summary.Id, orders[0].Id, Pdf);
        (await StageOf(orders[0].Id)).ShouldBe(PodStage.ProofUploaded);
        (await StageOf(orders[1].Id)).ShouldBe(PodStage.DeliveryPending);

        var before = await (await s.Admin.GetAsync("/api/v1/pod/ageing")).ReadAsync<AgeingDto>();
        before.Outstanding.ShouldBeGreaterThanOrEqualTo(1);
        before.Buckets[0].Count.ShouldBeGreaterThanOrEqualTo(1); // delivered today
        before.Transporters.ShouldContain(t => t.TransporterId == s.Transporter.Id);

        await s.Admin.PostAsync(Url(shipment, orders[0].Id, "pod/verify"), null);
        var after = await (await s.Admin.GetAsync("/api/v1/pod/ageing")).ReadAsync<AgeingDto>();
        after.Outstanding.ShouldBe(before.Outstanding - 1);
        (await StageOf(orders[0].Id)).ShouldBe(PodStage.ProofVerified);
    }

    [Fact]
    public async Task Closing_a_run_in_one_step_still_leaves_proof_to_collect()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var (shipment, orders) = await DispatchedAsync(s, 3_000m);

        var delivered = await s.Admin.PostAsync($"/api/v1/shipments/{shipment.Summary.Id}/deliver", null);
        delivered.StatusCode.ShouldBe(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());

        var line = (await delivered.ReadAsync<ShipmentDto>()).Orders.Single();
        line.DeliveredAt.ShouldNotBeNull();
        line.PodStatus.ShouldBe(PodStatus.Awaiting);
        (await Upload(s.Vendor, shipment.Summary.Id, orders[0].Id, Pdf)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
