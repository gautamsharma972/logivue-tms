using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.Modules.Deliveries.Domain;

namespace Tms.Modules.Deliveries.Application.Ocr;

/// <summary>Settings for the local Ollama OCR provider (<c>Deliveries:Ocr</c>). Off unless <see cref="Enabled"/> is set.</summary>
public sealed class OllamaOcrOptions
{
    public const string Section = "Deliveries:Ocr";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>A vision-language model. Qwen2.5-VL is the strongest open document reader that Ollama serves; any other vision model can be named here.</summary>
    public string Model { get; set; } = "qwen2.5vl:7b";

    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Reads a paper POD with a vision-language model served by Ollama, so the document never leaves the machine. A photograph is sent as an image; a scanned PDF has its
/// embedded page image taken out; a PDF with a text layer is sent as text. The model answers in a fixed JSON shape, and each field comes back as the model read it
/// with the model's own confidence. That confidence is self-reported, not calibrated: the checks against the delivery and the human review are the real controls.
/// </summary>
internal sealed class OllamaPodOcrService(HttpClient http, IOptions<OllamaOcrOptions> options, ILogger<OllamaPodOcrService> logger) : IPodOcrService
{
    private readonly OllamaOcrOptions _options = options.Value;

    public string Provider => $"ollama:{_options.Model}";

    public async Task<PodOcrExtraction> ExtractAsync(OcrDocument document, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await document.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var message = new JsonObject { ["role"] = "user" };
        var prompt = new StringBuilder(Prompt);
        if (PdfContent.IsPdf(bytes))
        {
            var text = PdfContent.ExtractText(bytes);
            var image = PdfContent.LargestJpeg(bytes);
            if (image is not null)
            {
                message["images"] = new JsonArray(Convert.ToBase64String(image));
            }
            else if (text.Length > 0)
            {
                prompt.Append("\n\nThe text of the document:\n").Append(text);
            }
            else
            {
                throw new InvalidOperationException("The PDF has neither a text layer nor an embedded page image that can be read.");
            }
        }
        else if (PdfContent.IsImage(bytes))
        {
            message["images"] = new JsonArray(Convert.ToBase64String(bytes));
        }
        else
        {
            prompt.Append("\n\nThe text of the document:\n").Append(Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 32_000)));
        }

        message["content"] = prompt.ToString();
        var request = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["messages"] = new JsonArray(message),
            ["format"] = Schema(),
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = 8192 },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), "api/chat"), request, timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Ollama is not reachable at {_options.BaseUrl}: {ex.Message}", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Ollama did not answer within {_options.TimeoutSeconds} seconds.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Ollama answered {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 300)]}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var fields = Parse(body, logger);
            if (fields.Count == 0)
            {
                throw new InvalidOperationException("The model found no readable fields in the document.");
            }

            return new PodOcrExtraction(Provider, fields, Math.Round(fields.Average(f => f.Confidence), 4), null);
        }
    }

    /// <summary>Turns Ollama's chat answer into field readings. Anything the model left empty or could not read is dropped, never invented.</summary>
    internal static IReadOnlyList<OcrFieldReading> Parse(string chatResponse, ILogger? logger = null)
    {
        var content = JsonNode.Parse(chatResponse)?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        JsonNode? answer;
        try
        {
            answer = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning("The model's answer was not valid JSON: {Reason}", ex.Message);
            return [];
        }

        var transcript = answer?["text"]?.ToString();
        logger?.LogDebug("The model transcribed {Length} character(s) of the document", transcript?.Length ?? 0);
        var readings = new List<OcrFieldReading>();
        foreach (var name in OcrReconciler.FieldNames)
        {
            if (answer?[Key(name)] is not JsonObject field)
            {
                continue;
            }

            var raw = field["value"]?.ToString().Trim();
            if (string.IsNullOrEmpty(raw) || string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var confidence = 0.5m;
            if (field["confidence"] is JsonNode c && decimal.TryParse(c.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                confidence = parsed > 1m ? parsed / 100m : parsed;
            }

            confidence = Math.Clamp(confidence, 0m, 1m);
            if (transcript is { Length: > 0 })
            {
                // A model that says "100% sure" says so about everything, so its word alone is not trusted. A value counts as read only if it also appears in the
                // model's own transcription of the page; one that does not appear was inferred, and is held below any review threshold.
                confidence = Squash(transcript).Contains(Squash(raw), StringComparison.Ordinal) ? Math.Min(confidence, GroundedCeiling) : Math.Min(confidence, UngroundedCeiling);
            }

            readings.Add(new OcrFieldReading(name, raw, Normalise(name, raw), confidence));
        }

        return readings;
    }

    /// <summary>The most confidence a value can have when it is found in the transcription: a model reading is never as certain as a digital text layer.</summary>
    internal const decimal GroundedCeiling = 0.97m;

    /// <summary>The most confidence a value can have when the transcription does not contain it.</summary>
    internal const decimal UngroundedCeiling = 0.4m;

    private static string Squash(string s) => new([.. s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    internal static string Key(string fieldName) => string.Concat(fieldName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((w, i) => i == 0 ? w.ToLowerInvariant() : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    private static string Normalise(string label, string raw) => label switch
    {
        OcrReconciler.ShipmentNumber or OcrReconciler.DeliveryNumber or OcrReconciler.InvoiceNumber or OcrReconciler.VehicleNumber => OcrReconciler.Code(raw),
        OcrReconciler.DeliveredQuantity or OcrReconciler.ShortQuantity => LeadingNumber(raw),
        _ => raw,
    };

    /// <summary>"95 cartons" is 95; "1,095" is 1095. Anything that does not start with a number is kept as written, for a person to read.</summary>
    private static string LeadingNumber(string raw)
    {
        var m = System.Text.RegularExpressions.Regex.Match(raw.Trim(), @"^[-+]?\d[\d,]*(\.\d+)?");
        return m.Success ? m.Value.Replace(",", string.Empty, StringComparison.Ordinal) : raw;
    }

    private static JsonObject Schema()
    {
        var properties = new JsonObject { ["text"] = new JsonObject { ["type"] = "string", ["description"] = "Every line of text on the page, in reading order, one per line" } };
        foreach (var name in OcrReconciler.FieldNames)
        {
            properties[Key(name)] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["value"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["confidence"] = new JsonObject { ["type"] = "number" } },
                ["required"] = new JsonArray("value", "confidence"),
            };
        }

        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray([(JsonNode)"text", .. OcrReconciler.FieldNames.Select(n => (JsonNode)Key(n))]) };
    }

    private const string Prompt = """
        You are reading a signed proof-of-delivery document (a delivery challan or receipt) from India.
        Transcribe the whole page into "text": every line, in reading order, one per line, exactly as printed or written, including both labels and values. Then read the fields from your transcription.
        Copy each value exactly as it appears, including letters, digits and dashes. If a field is not on the document, or you cannot read it, return null for its value. Never guess or infer a value.
        For each field give your confidence from 0 to 1 that you read it exactly right: use a low number for faint, smudged, handwritten or partly hidden text.
        Fields: shipmentNumber, deliveryNumber, invoiceNumber, customer, transporter, vehicleNumber, deliveryDate (as written), recipientName,
        deliveredQuantity (a number only), shortQuantity (a number only), damageRemarks.
        """;
}

/// <summary>Cheap looks inside a document, with no imaging library: what kind it is, its text, and the page picture of a scanned PDF.</summary>
internal static class PdfContent
{
    public static bool IsPdf(byte[] b) => b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46;

    public static bool IsImage(byte[] b) =>
        b.Length > 8 && ((b[0] == 0xFF && b[1] == 0xD8) || (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) || (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46));

    /// <summary>Text shown by simple PDFs: the strings inside parentheses. Compressed content streams are not unpacked, so a compressed PDF yields nothing.</summary>
    public static string ExtractText(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf, 0, Math.Min(pdf.Length, 4 * 1024 * 1024));
        var lines = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '(')
            {
                continue;
            }

            var sb = new StringBuilder();
            for (i++; i < text.Length && text[i] != ')'; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i++;
                }

                sb.Append(text[i]);
            }

            if (sb.Length > 1)
            {
                lines.Add(sb.ToString());
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>The biggest JPEG inside a PDF: a scanner's output is one full-page JPEG per page. Returns null when there is none.</summary>
    public static byte[]? LargestJpeg(byte[] pdf)
    {
        byte[]? best = null;
        var i = 0;
        while (i < pdf.Length - 3)
        {
            if (pdf[i] == 0xFF && pdf[i + 1] == 0xD8 && pdf[i + 2] == 0xFF)
            {
                var end = -1;
                for (var j = i + 3; j < pdf.Length - 1; j++)
                {
                    if (pdf[j] == 0xFF && pdf[j + 1] == 0xD9)
                    {
                        end = j + 2;
                        // a JPEG ends at the first EOI that is followed by the PDF's stream terminator
                        if (StartsWithEndStream(pdf, end))
                        {
                            break;
                        }
                    }
                }

                if (end > 0)
                {
                    var length = end - i;
                    if (length > 2048 && (best is null || length > best.Length))
                    {
                        best = pdf[i..end];
                    }

                    i = end;
                    continue;
                }
            }

            i++;
        }

        return best;
    }

    private static bool StartsWithEndStream(byte[] b, int at)
    {
        var k = at;
        while (k < b.Length && (b[k] == '\r' || b[k] == '\n' || b[k] == ' '))
        {
            k++;
        }

        ReadOnlySpan<byte> marker = "endstream"u8;
        return k + marker.Length <= b.Length && b.AsSpan(k, marker.Length).SequenceEqual(marker);
    }
}

/// <summary>
/// Chooses how a document is read: a digital POD that uses the labelled "Name: value" layout is read exactly from its text layer; anything else goes to Ollama
/// when it is enabled; otherwise the failure is reported for a person, never guessed.
/// </summary>
internal sealed class CompositePodOcrService(TextLayerOcrService textLayer, OllamaPodOcrService? ollama) : IPodOcrService
{
    public string Provider => ollama is null ? textLayer.Provider : $"{textLayer.Provider}+{ollama.Provider}";

    public async Task<PodOcrExtraction> ExtractAsync(OcrDocument document, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await document.Content.CopyToAsync(buffer, cancellationToken);

        try
        {
            buffer.Position = 0;
            return await textLayer.ExtractAsync(document with { Content = buffer }, cancellationToken);
        }
        catch (InvalidOperationException) when (ollama is not null)
        {
            buffer.Position = 0;
            return await ollama.ExtractAsync(document with { Content = buffer }, cancellationToken);
        }
    }
}
