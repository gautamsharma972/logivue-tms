using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Transporters.Application;

namespace Tms.IntegrationTests.Infrastructure;

/// <summary>Real, readable image bytes (the server checks size and signature), distinct per seed so each has its own hash.</summary>
internal static class TestImages
{
    public static byte[] Jpeg(int width = 800, int height = 600, int seed = 0)
    {
        byte[] head = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        return [.. head, .. BitConverter.GetBytes(seed), 0xFF, 0xD9];
    }

    /// <summary>A PNG signature canvas: blank (transparent) or with a stroke across it.</summary>
    public static byte[] Signature(bool drawn)
    {
        const int width = 60;
        const int height = 30;
        var raw = new byte[height * ((width * 4) + 1)];
        if (drawn)
        {
            for (var x = 5; x < 55; x++)
            {
                var o = (15 * ((width * 4) + 1)) + 1 + (x * 4);
                raw[o] = 10;
                raw[o + 1] = 10;
                raw[o + 2] = 90;
                raw[o + 3] = 255;
            }
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            z.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            png.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
            png.Write(Encoding.ASCII.GetBytes(type));
            png.Write(data);
            png.Write([0, 0, 0, 0]);
        }

        Chunk("IHDR", [0, 0, 0, width, 0, 0, 0, height, 8, 6, 0, 0, 0]);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    /// <summary>A paper POD with a text layer: each line is "Label: value [confidence%]".</summary>
    public static byte[] PaperPod(params string[] lines) =>
        Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\nBT /F1 12 Tf " + string.Join(' ', lines.Select(l => $"({l}) Tj")) + " ET\nendobj\n%%EOF");
}

/// <summary>A transporter with a vendor login, a second (rival) transporter's login, and helpers to open and drive deliveries.</summary>
internal sealed class DeliveryScenario : IDisposable
{
    public required HttpClient Admin { get; init; }

    public required HttpClient Vendor { get; init; }

    public required HttpClient Rival { get; init; }

    public required TransporterDto Transporter { get; init; }

    public required TransporterDto RivalTransporter { get; init; }

    public string ShipmentReference { get; } = $"SH-T{Guid.NewGuid():N}"[..14];

    public static async Task<DeliveryScenario> CreateAsync(TmsApiFactory factory)
    {
        var admin = await factory.AdminAsync();
        var transporter = await ContractApiData.ActiveTransporterAsync(admin);
        var rival = await ContractApiData.ActiveTransporterAsync(admin);
        return new DeliveryScenario
        {
            Admin = admin, Transporter = transporter, RivalTransporter = rival,
            Vendor = await VendorForAsync(factory, admin, transporter.Id), Rival = await VendorForAsync(factory, admin, rival.Id),
        };
    }

    private static async Task<HttpClient> VendorForAsync(TmsApiFactory factory, HttpClient admin, Guid transporterId)
    {
        var role = await admin.CreateExternalRoleAsync(DeliveryPermissions.Execute);
        var email = ApiExtensions.UniqueEmail("driver");
        var created = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(email, "Dispatcher", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporterId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);
    }

    public SaveDeliveryRequest Request(decimal quantity = 100, string? email = "customer@example.test", double? lat = null, double? lon = null, int? radius = null, DateTimeOffset? windowEnd = null,
        params (string Sku, decimal Qty)[] more) => new(
        ShipmentReference, "INV9001", null, null, "LR-000123", 1, Transporter.Id, Transporter.LegalName, null, "MH12AB1234", "Ramesh",
        "CUST-ABC", "ABC Distributors", "9876543210", email, "Pune", "Surat", "Plot 1, Surat", lat, lon, radius,
        DateTimeOffset.UtcNow.AddHours(2), DateTimeOffset.UtcNow, windowEnd ?? DateTimeOffset.UtcNow.AddHours(6),
        [new CreateDeliveryItemRequest("SKU-001", "Widgets", quantity, quantity, "PKG"), .. more.Select(m => new CreateDeliveryItemRequest(m.Sku, m.Sku, m.Qty, m.Qty, "PKG"))]);

    public async Task<DeliveryDto> DeliveryAsync(SaveDeliveryRequest? request = null)
    {
        var response = await Admin.PostJsonAsync("/api/v1/deliveries", request ?? Request());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<DeliveryDto>();
    }

    public static DeviceContext Here(string device = "device-1") => new(new GeoDto(18.5204, 73.8567, 12), device, null);

    public async Task<DeliveryDto> ArrivedAsync(SaveDeliveryRequest? request = null, HttpClient? by = null)
    {
        var delivery = await DeliveryAsync(request);
        var client = by ?? Vendor;
        (await client.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/start", Here())).StatusCode.ShouldBe(HttpStatusCode.OK);
        var arrived = await client.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/arrive", Here());
        arrived.StatusCode.ShouldBe(HttpStatusCode.OK, await arrived.Content.ReadAsStringAsync());
        return await arrived.ReadAsync<DeliveryDto>();
    }

    public static ItemQuantityRequest Qty(DeliveryItemDto item, decimal delivered, decimal @short = 0, decimal damaged = 0, decimal rejected = 0) =>
        new(item.Id, delivered, @short, damaged, rejected, @short > 0 ? "SHORT_LOADED" : null, damaged > 0 ? "BROKEN" : null, damaged > 0 ? "Crushed in transit" : null, null, null);

    public static CompleteDeliveryRequest Complete(DeliveryOutcome outcome, ProofRequest? proof = null, RemainingDisposition? disposition = null, params ItemQuantityRequest[] items) =>
        new(outcome, items, disposition, "Delivered at the gate", proof ?? new ProofRequest(ProofMethod.Photo, "Anil Kumar", "Store manager", "9876543210", null, false, false), Here());

    public async Task<HttpResponseMessage> CompleteAsync(DeliveryDto d, CompleteDeliveryRequest request, HttpClient? by = null) =>
        await (by ?? Vendor).PostJsonAsync($"/api/v1/deliveries/{d.Summary.Id}/complete", request);

    public async Task<PodDto> CompletedAsync(DeliveryDto arrived, DeliveryOutcome outcome = DeliveryOutcome.Full, ProofRequest? proof = null, params ItemQuantityRequest[] items)
    {
        var request = Complete(outcome, proof, null, items.Length > 0 ? items : [.. arrived.Items.Select(i => Qty(i, i.DispatchedQuantity))]);
        var response = await CompleteAsync(arrived, request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var delivery = await response.ReadAsync<DeliveryDto>();
        delivery.Summary.PodId.ShouldNotBeNull();
        return await (await Vendor.GetAsync($"/api/v1/pods/{delivery.Summary.PodId}")).ReadAsync<PodDto>();
    }

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid podId, byte[] bytes, EvidenceType type = EvidenceType.PackagePhoto, string? idempotencyKey = null, string contentType = "image/jpeg")
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(type.ToString()), "type" },
            { new StringContent("18.5204"), "latitude" },
            { new StringContent("73.8567"), "longitude" },
            { new StringContent("device-1"), "deviceReference" },
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType); // deliberately not trusted by the server
        form.Add(file, "file", "capture");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pods/{podId}/evidence") { Content = form };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> SignAsync(HttpClient client, Guid podId, byte[] png, string signer = "Anil Kumar")
    {
        using var form = new MultipartFormDataContent { { new StringContent(signer), "signerName" } };
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "signature.png");
        return await client.PostAsync($"/api/v1/pods/{podId}/signature", form);
    }

    /// <summary>Uploads a photo (a different one every time, since a repeated file is flagged as a duplicate across proofs) and submits.</summary>
    public async Task<PodDto> PhotoAndSubmitAsync(PodDto pod, int? seed = null, HttpClient? by = null)
    {
        var client = by ?? Vendor;
        (await UploadAsync(client, pod.Summary.Id, TestImages.Jpeg(seed: seed ?? Random.Shared.Next()))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var submitted = await client.PostAsync($"/api/v1/pods/{pod.Summary.Id}/submit", null);
        submitted.StatusCode.ShouldBe(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        return await submitted.ReadAsync<PodDto>();
    }

    public async Task<PodDto> PodAsync(Guid id, HttpClient? by = null) => await (await (by ?? Admin).GetAsync($"/api/v1/pods/{id}")).ReadAsync<PodDto>();

    public async Task<PodDto> WaitForAsync(Guid podId, Func<PodDto, bool> done)
    {
        PodDto pod = await PodAsync(podId);
        for (var i = 0; i < 100 && !done(pod); i++)
        {
            await Task.Delay(100);
            pod = await PodAsync(podId);
        }

        done(pod).ShouldBeTrue($"Last status: {pod.Summary.Status}, ocr: {string.Join(",", pod.Ocr.Select(o => o.Status))}");
        return pod;
    }

    public async Task SetSettingAsync(string key, object value)
    {
        var response = await Admin.PutJsonAsync($"/api/v1/delivery-settings/{key}", value);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        Admin.Dispose();
        Vendor.Dispose();
        Rival.Dispose();
    }
}
