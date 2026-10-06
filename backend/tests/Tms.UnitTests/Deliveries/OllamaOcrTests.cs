using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tms.Modules.Deliveries.Application.Ocr;

namespace Tms.UnitTests.Deliveries;

public class OllamaOcrTests
{
    private static string Chat(object answer) => JsonSerializer.Serialize(new { message = new { role = "assistant", content = JsonSerializer.Serialize(answer) } });

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Loops(int times, string good) : HttpMessageHandler
    {
        public List<string> Temperatures { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Temperatures.Add(System.Text.RegularExpressions.Regex.Match(body, "\"temperature\":([0-9.]+)").Groups[1].Value);
            return Temperatures.Count <= times
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":\"prediction aborted, token repeat limit reached\"}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(good, Encoding.UTF8, "application/json") };
        }
    }

    private static OllamaPodOcrService Service(HttpMessageHandler stub) =>
        new(new HttpClient(stub), Options.Create(new OllamaOcrOptions { Enabled = true, Model = "qwen2.5vl:7b" }), NullLogger<OllamaPodOcrService>.Instance);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public void Reads_fields_with_the_models_own_confidence_and_drops_what_it_could_not_read()
    {
        var fields = OllamaPodOcrService.Parse(Chat(new
        {
            text = "Shipment SH-10025 vehicle MH 12 AB 1234 received 1,095 cartons",
            shipmentNumber = new { value = "SH-10025", confidence = 0.98 },
            vehicleNumber = new { value = "MH 12 AB 1234", confidence = 96 },
            deliveredQuantity = new { value = "1,095", confidence = 0.64 },
            recipientName = new { value = (string?)null, confidence = 0.9 },
            invoiceNumber = new { value = "", confidence = 0.9 },
        }));

        fields.Select(f => f.Name).ShouldBe(["Shipment Number", "Vehicle Number", "Delivered Quantity"]);
        fields.First(f => f.Name == "Shipment Number").Normalized.ShouldBe("SH10025");
        fields.First(f => f.Name == "Vehicle Number").Confidence.ShouldBe(0.96m); // a percentage is understood
        fields.First(f => f.Name == "Delivered Quantity").Normalized.ShouldBe("1095");
    }

    [Fact]
    public void A_value_the_model_cannot_find_in_its_own_transcription_is_held_below_any_review_threshold()
    {
        var fields = OllamaPodOcrService.Parse(Chat(new
        {
            text = "Delivery challan  Shipment SH10025  Received 95 cartons",
            shipmentNumber = new { value = "SH10025", confidence = 1 },
            vehicleNumber = new { value = "MH12AB1234", confidence = 1 }, // never on the page: inferred
            deliveredQuantity = new { value = "95 cartons", confidence = 1 },
        }));

        fields.First(f => f.Name == "Shipment Number").Confidence.ShouldBe(OllamaPodOcrService.GroundedCeiling); // a model is never as sure as a text layer
        fields.First(f => f.Name == "Vehicle Number").Confidence.ShouldBe(OllamaPodOcrService.UngroundedCeiling);
        fields.First(f => f.Name == "Delivered Quantity").Normalized.ShouldBe("95");
    }

    [Fact]
    public void One_value_filling_two_fields_is_a_guessed_label_and_both_are_held_for_a_person()
    {
        var fields = OllamaPodOcrService.Parse(Chat(new
        {
            text = "Invoice INV-5512 vehicle GJ05CD7788 customer Surat Retail Hub",
            invoiceNumber = new { value = "INV-5512", confidence = 1 },
            deliveryNumber = new { value = "INV-5512", confidence = 1 }, // the invoice number again
            vehicleNumber = new { value = "GJ05CD7788", confidence = 1 },
            transporter = new { value = "GJ05CD7788", confidence = 1 }, // a plate is not a carrier
            customer = new { value = "Surat Retail Hub", confidence = 1 },
        }));

        fields.Where(f => f.Name is "Invoice Number" or "Delivery Number" or "Vehicle Number" or "Transporter").ShouldAllBe(f => f.Confidence == OllamaPodOcrService.UngroundedCeiling);
        fields.First(f => f.Name == "Customer").Confidence.ShouldBe(OllamaPodOcrService.GroundedCeiling); // untouched
    }

    [Fact]
    public void A_quantity_written_as_a_word_for_nothing_is_zero()
    {
        var fields = OllamaPodOcrService.Parse(Chat(new { text = "Short: nil Received 240 bags", shortQuantity = new { value = "nil", confidence = 1 }, deliveredQuantity = new { value = "240 bags", confidence = 1 } }));

        fields.First(f => f.Name == "Short Quantity").Normalized.ShouldBe("0");
        fields.First(f => f.Name == "Delivered Quantity").Normalized.ShouldBe("240");
    }

    [Fact]
    public void An_answer_that_is_not_json_yields_nothing_rather_than_a_guess()
    {
        OllamaPodOcrService.Parse(JsonSerializer.Serialize(new { message = new { content = "I think the shipment is SH1" } })).ShouldBeEmpty();
        OllamaPodOcrService.Parse("{}").ShouldBeEmpty();
    }

    [Fact]
    public async Task Sends_a_photograph_as_an_image_to_the_configured_model()
    {
        var stub = new Stub(HttpStatusCode.OK, Chat(new { shipmentNumber = new { value = "SH10025", confidence = 0.9 } }));

        var result = await Service(stub).ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default);

        result.Provider.ShouldBe("ollama:qwen2.5vl:7b");
        result.Fields.ShouldHaveSingleItem().Raw.ShouldBe("SH10025");
        stub.Request!.ShouldContain("\"images\"");
        stub.Request!.ShouldContain("qwen2.5vl:7b");
        stub.Request!.ShouldContain("\"temperature\":0");
    }

    [Fact]
    public void The_title_of_the_page_is_not_a_delivery_number_and_no_damage_is_not_a_damage_remark()
    {
        var fields = OllamaPodOcrService.Parse(Chat(new
        {
            text = "DELIVERY CHALLAN Shipment SH10077 No damage noted.",
            deliveryNumber = new { value = "DELIVERY CHALLAN", confidence = 1 },
            shipmentNumber = new { value = "SH10077", confidence = 1 },
            damageRemarks = new { value = "No damage noted.", confidence = 1 },
        }));

        fields.First(f => f.Name == "Delivery Number").Confidence.ShouldBe(OllamaPodOcrService.UngroundedCeiling); // no digit: not a number
        fields.First(f => f.Name == "Shipment Number").Confidence.ShouldBe(OllamaPodOcrService.GroundedCeiling);
        fields.ShouldNotContain(f => f.Name == "Damage Remarks");
        OllamaPodOcrService.Parse(Chat(new { text = "No. 5 carton broken", damageRemarks = new { value = "No. 5 carton broken", confidence = 1 } })).ShouldContain(f => f.Name == "Damage Remarks"); // a real remark is kept
    }

    [Fact]
    public async Task A_model_that_loops_is_asked_again_a_little_warmer_and_gives_up_only_after_three_tries()
    {
        var good = Chat(new { text = "Shipment SH10025", shipmentNumber = new { value = "SH10025", confidence = 1 } });
        var twice = new Loops(2, good);
        (await Service(twice).ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default)).Fields.ShouldHaveSingleItem();
        twice.Temperatures.ShouldBe(["0.1", "0.3", "0.5"]);

        var always = new Loops(99, good);
        var failed = await Should.ThrowAsync<InvalidOperationException>(() => Service(always).ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default));
        failed.Message.ShouldContain("repeat");
        always.Temperatures.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_server_error_or_an_empty_reading_is_reported_for_a_person()
    {
        var down = await Should.ThrowAsync<InvalidOperationException>(() => Service(new Stub(HttpStatusCode.InternalServerError, "model not found")).ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default));
        down.Message.ShouldContain("500");

        var empty = await Should.ThrowAsync<InvalidOperationException>(() => Service(new Stub(HttpStatusCode.OK, Chat(new { shipmentNumber = new { value = (string?)null, confidence = 0.1 } }))).ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default));
        empty.Message.ShouldContain("no readable fields");
    }

    [Fact]
    public void Takes_the_page_picture_out_of_a_scanned_pdf()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(Enumerable.Repeat((byte)7, 3000)).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
        var pdf = Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\nstream\n").Concat(jpeg).Concat(Encoding.Latin1.GetBytes("\nendstream\nendobj\n%%EOF")).ToArray();

        PdfContent.IsPdf(pdf).ShouldBeTrue();
        PdfContent.LargestJpeg(pdf).ShouldBe(jpeg);
        PdfContent.LargestJpeg(Encoding.Latin1.GetBytes("%PDF-1.4 no pictures")).ShouldBeNull();
    }

    [Fact]
    public async Task A_digital_pod_in_the_labelled_layout_is_read_exactly_without_calling_the_model()
    {
        var stub = new Stub(HttpStatusCode.OK, Chat(new { }));
        var composite = new CompositePodOcrService(new TextLayerOcrService(), Service(stub));
        var doc = new OcrDocument("p.txt", "text/plain", new MemoryStream(Encoding.UTF8.GetBytes("Shipment Number: SH10025 [98%]\nVehicle Number: MH12AB1234 [96%]")));

        var result = await composite.ExtractAsync(doc, default);

        result.Provider.ShouldBe("text-layer");
        stub.Request.ShouldBeNull();
    }

    [Fact]
    public async Task A_photograph_falls_through_to_the_model_and_without_one_is_reported()
    {
        var stub = new Stub(HttpStatusCode.OK, Chat(new { shipmentNumber = new { value = "SH10025", confidence = 0.9 } }));
        var withModel = new CompositePodOcrService(new TextLayerOcrService(), Service(stub));
        (await withModel.ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default)).Provider.ShouldStartWith("ollama:");

        var without = new CompositePodOcrService(new TextLayerOcrService(), null);
        await Should.ThrowAsync<InvalidOperationException>(() => without.ExtractAsync(new OcrDocument("p.png", "image/png", new MemoryStream(Png)), default));
    }
}
