using System.Net;
using System.Net.Http.Headers;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Dashboard;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.DeliveryScenario;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeliveryGapsApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Items_can_be_listed_and_checked_against_the_dispatch_without_saving_anything()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();
        var item = delivery.Items.Single();

        var items = await (await s.Admin.GetAsync($"/api/v1/deliveries/{delivery.Summary.Id}/items")).ReadAsync<List<DeliveryItemDto>>();
        items.Single().DispatchedQuantity.ShouldBe(100);
        items.Single().DeliveredQuantity.ShouldBeNull();

        var short5 = await s.Admin.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/items/reconcile", new ReconcileItemsRequest([Qty(item, 92, 5, 3)]));
        var ok = (await short5.ReadAsync<List<ReconciliationDto>>()).Single();
        (ok.Reconciled, ok.Accounted, ok.Unaccounted).ShouldBe((true, 100m, 0m)); // 92 + 5 + 3 = 100

        var off = (await (await s.Admin.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/items/reconcile", new ReconcileItemsRequest([Qty(item, 90, 5, 3)]))).ReadAsync<List<ReconciliationDto>>()).Single();
        (off.Reconciled, off.Unaccounted).ShouldBe((false, 2m)); // the exact difference, never corrected
        off.Problems.ShouldNotBeEmpty();

        (await s.Admin.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/items/reconcile", new ReconcileItemsRequest([Qty(item, -1)]))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await (await s.Admin.GetAsync($"/api/v1/deliveries/{delivery.Summary.Id}")).ReadAsync<DeliveryDto>();
        after.Items.Single().DeliveredQuantity.ShouldBeNull(); // a dry run saved nothing

        (await s.Rival.GetAsync($"/api/v1/deliveries/{delivery.Summary.Id}/items")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_proof_can_be_started_through_the_pods_collection()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var response = await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Full, null, null, [.. arrived.Items.Select(i => Qty(i, i.DispatchedQuantity))]));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var created = (await response.ReadAsync<DeliveryDto>()).Summary.PodId!.Value;

        var again = await s.Admin.PostJsonAsync("/api/v1/pods", new CreatePodRequest(arrived.Summary.Id));
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        (await again.ReadAsync<PodDto>()).Summary.Id.ShouldBe(created); // the same proof: starting it twice does not make a second one
    }

    [Fact]
    public async Task The_list_filters_by_vehicle_lane_and_service_type()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync(s.Request() with { ServiceType = "PTL", VehicleReference = "MH12ZZ9999", OriginReference = "Nashik", DestinationReference = "Nagpur" });
        delivery.Summary.ServiceType.ShouldBe("PTL");

        async Task<int> Count(string query) => (await (await s.Admin.GetAsync($"/api/v1/deliveries?pageSize=100&{query}")).ReadAsync<PagedResult<DeliverySummaryDto>>()).Items.Count(d => d.Id == delivery.Summary.Id);

        (await Count("vehicle=ZZ9999")).ShouldBe(1);
        (await Count("vehicle=NOPE")).ShouldBe(0);
        (await Count("lane=Nagpur")).ShouldBe(1);
        (await Count("lane=Nashik")).ShouldBe(1);
        (await Count("lane=Mumbai")).ShouldBe(0);
        (await Count("serviceType=PTL")).ShouldBe(1);
        (await Count("serviceType=FTL")).ShouldBe(0);
    }

    [Fact]
    public async Task Compliance_and_reports_can_be_narrowed_by_vehicle_and_service_type()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync(s.Request() with { ServiceType = "Dedicated", VehicleReference = "MH12QQ1111" });
        await s.ArrivedAsync(s.Request() with { ServiceType = "Dedicated", VehicleReference = "MH12QQ1111", ShipmentReference = delivery.Summary.ShipmentReference + "x" });

        var all = await (await s.Admin.GetAsync("/api/v1/pod-dashboard/compliance?groupBy=customer")).ReadAsync<ComplianceDto>();
        var none = await (await s.Admin.GetAsync("/api/v1/pod-dashboard/compliance?groupBy=customer&vehicle=NO-SUCH-VEHICLE")).ReadAsync<ComplianceDto>();
        var other = await (await s.Admin.GetAsync("/api/v1/pod-dashboard/compliance?groupBy=customer&serviceType=NoSuchService")).ReadAsync<ComplianceDto>();
        none.Rows.ShouldBeEmpty();
        other.Rows.ShouldBeEmpty();
        all.Overall.ShouldNotBeNull();

        var csv = await s.Admin.GetAsync("/api/v1/delivery-reports/deliveries?format=csv&vehicle=QQ1111&serviceType=Dedicated");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await csv.Content.ReadAsStringAsync()).ShouldContain("MH12QQ1111");
        (await (await s.Admin.GetAsync("/api/v1/delivery-reports/deliveries?format=csv&vehicle=NO-SUCH-VEHICLE")).Content.ReadAsStringAsync()).ShouldNotContain("MH12QQ1111");
    }

    [Fact]
    public async Task A_photo_that_fails_the_security_scan_is_refused_and_a_qr_scanned_code_counts_as_a_verified_code()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var pod = await s.CompletedAsync(await s.ArrivedAsync());

        var refused = await UploadAsync(s.Admin, pod.Summary.Id, [.. TestImages.Jpeg(seed: 91), .. System.Text.Encoding.ASCII.GetBytes(MarkerFileScanner.Marker)]);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("pods.file_infected");
        (await UploadAsync(s.Admin, pod.Summary.Id, TestImages.Jpeg(seed: 92))).StatusCode.ShouldBe(HttpStatusCode.Created); // a clean one is fine

        var proof = await s.Admin.PutJsonAsync($"/api/v1/pods/{pod.Summary.Id}/proof", new UpdateProofRequest(new ProofRequest(ProofMethod.Qr, "Anil Kumar", "Manager", null, null, true, false)));
        proof.StatusCode.ShouldBe(HttpStatusCode.OK, await proof.Content.ReadAsStringAsync());
        var submit = await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);
        submit.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // a QR proof needs the delivery code verified first, like a typed code
        (await submit.Content.ReadAsStringAsync()).ShouldContain("one-time code");
    }

    [Fact]
    public async Task A_file_can_be_attached_to_an_exception_and_is_served_privately()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();
        var raised = await (await s.Admin.PostJsonAsync("/api/v1/delivery-exceptions", new RaiseExceptionRequest(delivery.Summary.Id, ExceptionType.AddressIssue, "Gate locked", null))).ReadAsync<ExceptionDto>();
        var id = raised.Summary.Id;

        static async Task<HttpResponseMessage> Attach(HttpClient client, Guid id, byte[] bytes, string? note = null)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg"); // not trusted
            form.Add(file, "file", "gate.jpg");
            if (note is not null)
            {
                form.Add(new StringContent(note), "note");
            }

            return await client.PostAsync($"/api/v1/delivery-exceptions/{id}/attachments", form);
        }

        var jpeg = TestImages.Jpeg(seed: 41);
        var attached = await Attach(s.Admin, id, jpeg, "Photo of the locked gate");
        attached.StatusCode.ShouldBe(HttpStatusCode.OK, await attached.Content.ReadAsStringAsync());
        var dto = await attached.ReadAsync<ExceptionDto>();
        var file = dto.Attachments!.Single();
        (file.FileName, file.ContentType, file.Note).ShouldBe(("gate.jpg", "image/jpeg", "Photo of the locked gate"));
        dto.Notes.Select(n => n.Text).ShouldContain("Attached gate.jpg.");

        (await Attach(s.Admin, id, jpeg)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // the same file twice
        (await Attach(s.Admin, id, "not an image or pdf"u8.ToArray())).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // judged by its bytes, not its name
        var infected = await Attach(s.Admin, id, [.. TestImages.Jpeg(seed: 77), .. System.Text.Encoding.ASCII.GetBytes(MarkerFileScanner.Marker)]);
        infected.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // the scanner hook refuses it before it is stored
        (await infected.Content.ReadAsStringAsync()).ShouldContain("exceptions.file_infected");

        var download = await s.Admin.GetAsync($"/api/v1/exception-attachments/{file.Id}/file");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(jpeg);

        (await s.Rival.GetAsync($"/api/v1/exception-attachments/{file.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Attach(s.Vendor, id, TestImages.Jpeg(seed: 42))).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // the vendor sees the exception but does not manage it
        (await (await s.Vendor.GetAsync($"/api/v1/delivery-exceptions/{id}")).ReadAsync<ExceptionDto>()).Attachments.ShouldNotBeNull();

        await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/resolve", new ResolveExceptionRequest("Rebooked", null, ResponsibleParty.Customer, null, null, null));
        (await Attach(s.Admin, id, TestImages.Jpeg(seed: 43))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // nothing is added to a resolved exception
    }
}
