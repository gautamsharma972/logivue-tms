using System.IO.Compression;
using System.Text;
using Tms.Modules.Deliveries.Application.Ocr;
using Tms.Modules.Deliveries.Domain;
using static Tms.UnitTests.Deliveries.DeliveryTestData;

namespace Tms.UnitTests.Deliveries;

public class OcrAndEvidenceTests
{
    private static readonly OcrSetting Ocr = (OcrSetting)DeliverySettingDefaults.For(DeliverySettingKeys.Ocr)!;

    private static (Delivery Delivery, PodRecord Pod) Case()
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, Qty(d, 95, @short: 3) with { DamagedQuantity = 2, DamageType = "BROKEN", DamageReason = "Crushed" });
        return (d, PodFor(d, recipient: "Ramesh Patel"));
    }

    private static IReadOnlyList<OcrCheck> Run(Delivery d, PodRecord pod, params OcrReading[] readings) => OcrReconciler.Reconcile(readings, d, pod, Ocr);

    [Fact]
    public void A_field_that_agrees_with_the_system_and_is_read_confidently_matches()
    {
        var (d, pod) = Case();
        var checks = Run(d, pod,
            new(OcrReconciler.ShipmentNumber, "SH-00010", "SH00010", 0.98m),
            new(OcrReconciler.VehicleNumber, "MH 12 AB 1234", "MH12AB1234", 0.96m),
            new(OcrReconciler.RecipientName, "Ramesh", "Ramesh", 0.90m));

        checks.ShouldAllBe(c => c.Status == OcrFieldStatus.Matched);
    }

    [Fact]
    public void A_low_confidence_reading_goes_to_review_even_if_it_happens_to_agree()
    {
        var (d, pod) = Case();
        var check = Run(d, pod, new OcrReading(OcrReconciler.DeliveredQuantity, "95", "95", 0.64m)).Single();

        check.Status.ShouldBe(OcrFieldStatus.LowConfidence);
        check.Message!.ShouldContain("64%");
    }

    [Fact]
    public void Thresholds_differ_by_how_critical_the_field_is()
    {
        OcrReconciler.ThresholdFor(OcrReconciler.ShipmentNumber, Ocr).ShouldBe(0.95m);
        OcrReconciler.ThresholdFor(OcrReconciler.RecipientName, Ocr).ShouldBe(0.85m);
        OcrReconciler.ThresholdFor(OcrReconciler.DamageRemarks, Ocr).ShouldBe(0.70m);

        var (d, pod) = Case();
        Run(d, pod, new OcrReading(OcrReconciler.ShipmentNumber, "SH-00010", "SH00010", 0.90m)).Single().Status.ShouldBe(OcrFieldStatus.LowConfidence);
        Run(d, pod, new OcrReading(OcrReconciler.DamageRemarks, "2 broken", "2 broken", 0.75m)).Single().Status.ShouldBe(OcrFieldStatus.NotChecked);
    }

    [Fact]
    public void A_value_that_disagrees_with_the_system_is_a_mismatch_that_shows_both()
    {
        var (d, pod) = Case();
        var check = Run(d, pod, new OcrReading(OcrReconciler.DeliveredQuantity, "92", "92", 0.99m), new OcrReading(OcrReconciler.VehicleNumber, "MH12XX9999", "MH12XX9999", 0.99m));

        check[0].Status.ShouldBe(OcrFieldStatus.Mismatch);
        check[0].Message.ShouldBe("The paper says '92'; the system has '95'.");
        check[1].Status.ShouldBe(OcrFieldStatus.Mismatch);
    }

    [Fact]
    public void Dates_in_the_usual_indian_formats_are_compared_as_dates()
    {
        var (d, pod) = Case();
        var day = d.ActualDeliveryAt!.Value.ToOffset(TimeSpan.FromMinutes(330));
        var written = day.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Run(d, pod, new OcrReading(OcrReconciler.DeliveryDate, written, written, 0.97m)).Single().Status.ShouldBe(OcrFieldStatus.Matched);
        Run(d, pod, new OcrReading(OcrReconciler.DeliveryDate, "01/01/2020", "01/01/2020", 0.97m)).Single().Status.ShouldBe(OcrFieldStatus.Mismatch);

        // the other ways a date is written on a challan
        foreach (var format in new[] { "dd MM yyyy", "dd.MM.yyyy", "dd/MM/yy", "dd-MMM-yyyy", "d MMMM yyyy", "dd-MMM-yy" })
        {
            var text = day.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            Run(d, pod, new OcrReading(OcrReconciler.DeliveryDate, text, text, 0.97m)).Single().Status.ShouldBe(OcrFieldStatus.Matched, text);
        }
    }

    [Fact]
    public void A_field_with_nothing_to_compare_against_is_not_checked_rather_than_failed()
    {
        var (d, pod) = Case();
        Run(d, pod, new OcrReading(OcrReconciler.Transporter, "Some Carrier", "Some Carrier", 0.99m)).Single().Status.ShouldBe(OcrFieldStatus.Mismatch); // known transporter, differs
        Run(d, pod, new OcrReading(OcrReconciler.DamageRemarks, "2 broken", "2 broken", 0.99m)).Single().Status.ShouldBe(OcrFieldStatus.NotChecked);
    }

    private static async Task<PodOcrExtraction> ReadAsync(string content) =>
        await new TextLayerOcrService().ExtractAsync(new OcrDocument("pod.txt", "text/plain", new MemoryStream(Encoding.UTF8.GetBytes(content))), CancellationToken.None);

    [Fact]
    public async Task The_text_layer_reader_extracts_labelled_fields_and_honours_confidence_hints()
    {
        var read = await ReadAsync("Shipment Number: SH-00010 [98%]\nVehicle Number: MH12AB1234 [96%]\nDelivered Quantity: 95 [64%]\nRecipient Name: Ramesh Patel\nIrrelevant line");

        read.Provider.ShouldBe("text-layer");
        read.Fields.Select(f => (f.Name, f.Confidence)).ShouldBe([("Shipment Number", 0.98m), ("Vehicle Number", 0.96m), ("Delivered Quantity", 0.64m), ("Recipient Name", 0.97m)]);
        read.Fields[0].Normalized.ShouldBe("SH00010");
        read.OverallConfidence.ShouldBe(Math.Round((0.98m + 0.96m + 0.64m + 0.97m) / 4, 4));
    }

    [Fact]
    public async Task The_text_layer_reader_reads_the_text_of_a_pdf()
    {
        var pdf = Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\nBT /F1 12 Tf (Shipment Number: SH-00010) Tj (Delivered Quantity: 95 [64%]) Tj ET\nendobj\n%%EOF");
        var read = await new TextLayerOcrService().ExtractAsync(new OcrDocument("pod.pdf", "application/pdf", new MemoryStream(pdf)), CancellationToken.None);

        read.Fields.Select(f => f.Name).ShouldBe(["Shipment Number", "Delivered Quantity"]);
    }

    [Fact]
    public async Task A_photograph_or_a_document_with_nothing_readable_is_a_failure_not_a_guess()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await new TextLayerOcrService().ExtractAsync(new OcrDocument("p.jpg", "image/jpeg", new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3])), CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(async () => await ReadAsync("nothing useful here"));
    }

    [Fact]
    public void A_reviewers_correction_sits_beside_the_original_reading()
    {
        var (_, pod) = Case();
        var evidence = AddPhoto(pod, "doc", EvidenceType.PodDocument);
        var result = pod.QueueOcr(evidence, "text-layer", Now);
        result.Complete("text-layer", [("Delivered Quantity", "95", "95", 0.64m, null)], 0.64m, null, Now);

        var field = result.ReviewField("delivered quantity", "98", Guid.NewGuid(), Now).Value;

        field.RawValue.ShouldBe("95");
        field.NormalizedValue.ShouldBe("95");
        field.ReviewedValue.ShouldBe("98");
        field.EffectiveValue.ShouldBe("98");
        result.ReviewField("Nope", "1", null, Now).Error.Code.ShouldBe("ocr.field_not_found");
        result.ReviewField("Delivered Quantity", " ", null, Now).Error.Code.ShouldBe("ocr.value_required");
    }

    private static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var raw = new byte[height * ((width * 4) + 1)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var o = (y * ((width * 4) + 1)) + 1 + (x * 4);
                raw[o] = r;
                raw[o + 1] = g;
                raw[o + 2] = b;
                raw[o + 3] = a;
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
            png.Write([0, 0, 0, 0]); // CRC is not checked by the probe
        }

        Chunk("IHDR", [(byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width, (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, 6, 0, 0, 0]);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    [Fact]
    public void A_blank_signature_canvas_is_recognised_and_a_drawn_one_is_not()
    {
        SignatureInk.HasInk(Png(60, 30, (_, _) => (0, 0, 0, 0))).ShouldBeFalse(); // transparent
        SignatureInk.HasInk(Png(60, 30, (_, _) => (255, 255, 255, 255))).ShouldBeFalse(); // white
        SignatureInk.HasInk(Png(60, 30, (x, y) => x == 20 && y is > 5 and < 25 ? ((byte)10, (byte)10, (byte)80, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0))).ShouldBeTrue();
        SignatureInk.HasInk([1, 2, 3]).ShouldBeFalse();
    }

    [Fact]
    public void Image_size_is_read_from_the_headers_and_garbage_is_unreadable()
    {
        ImageProbe.Size(Png(640, 480, (_, _) => (1, 2, 3, 255))).ShouldBe((640, 480));
        ImageProbe.Size([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]).ShouldBeNull();
        ImageProbe.Size("not an image"u8).ShouldBeNull();

        // JPEG: SOI, an APP0 segment, then a SOF0 marker holding 480 x 640.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        ImageProbe.Size(jpeg).ShouldBe((640, 480));
    }

    [Fact]
    public void The_geofence_distinguishes_inside_outside_and_cannot_tell()
    {
        const double lat = 18.5204;
        const double lon = 73.8567;
        Geo.Check(lat, lon, 200, lat + 0.0005, lon, 10, 100).ShouldBe(GeofenceStatus.Inside); // ~55 m
        Geo.Check(lat, lon, 200, lat + 0.01, lon, 10, 100).ShouldBe(GeofenceStatus.Outside); // ~1.1 km
        Geo.Check(lat, lon, 200, null, null, null, 100).ShouldBe(GeofenceStatus.GpsUnavailable);
        Geo.Check(lat, lon, 200, lat, lon, 500, 100).ShouldBe(GeofenceStatus.AccuracyInsufficient);
        Geo.Check(null, null, null, lat, lon, 5, 100).ShouldBe(GeofenceStatus.NotApplicable);
        Geo.Check(lat, lon, null, lat, lon, 5, 100).ShouldBe(GeofenceStatus.NotApplicable);
        Geo.DistanceMetres(lat, lon, lat, lon).ShouldBe(0);
    }
}
