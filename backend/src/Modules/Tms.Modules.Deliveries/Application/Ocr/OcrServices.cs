using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tms.Modules.Deliveries.Application.Pods;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Ocr;

public sealed record OcrDocument(string FileName, string ContentType, Stream Content);

public sealed record OcrFieldReading(string Name, string? Raw, string? Normalized, decimal Confidence, string? BoundingBoxJson = null);

public sealed record PodOcrExtraction(string Provider, IReadOnlyList<OcrFieldReading> Fields, decimal? OverallConfidence, string? RawResponse);

/// <summary>Reads a paper POD. Replaceable: AWS Textract, Azure Document Intelligence, Google Document AI or any other provider can sit behind this.</summary>
public interface IPodOcrService
{
    string Provider { get; }

    Task<PodOcrExtraction> ExtractAsync(OcrDocument document, CancellationToken cancellationToken);
}

/// <summary>
/// The provider used until a real OCR service is configured. It reads the document's own text layer (a digitally generated PDF or text file), and understands an
/// optional "[96%]" tag after a value as that field's confidence, so demo and test documents can show low-confidence cases. It cannot read a photograph: that is
/// reported as a failure for a person to handle, never guessed.
/// </summary>
internal sealed partial class TextLayerOcrService : IPodOcrService
{
    private static readonly string[] Labels = [.. OcrReconciler.FieldNames];

    public string Provider => "text-layer";

    public async Task<PodOcrExtraction> ExtractAsync(OcrDocument document, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await document.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var text = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 4 * 1024 * 1024));

        var pieces = new List<string>();
        if (bytes.Length > 4 && bytes[0] == 0x25 && bytes[1] == 0x50)
        {
            pieces.AddRange(PdfString().Matches(text).Select(m => Unescape(m.Groups[1].Value)));
        }
        else if (bytes.Length > 3 && (bytes[0] == 0xFF || bytes[0] == 0x89))
        {
            throw new InvalidOperationException("The document is an image with no text layer; a real OCR provider is needed to read it.");
        }
        else
        {
            pieces.AddRange(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        }

        var fields = new List<OcrFieldReading>();
        foreach (var piece in pieces)
        {
            var m = Line().Match(piece.Trim());
            if (!m.Success)
            {
                continue;
            }

            var label = Labels.FirstOrDefault(l => string.Equals(l, m.Groups["label"].Value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (label is null || fields.Any(f => f.Name == label))
            {
                continue;
            }

            var raw = m.Groups["value"].Value.Trim();
            var confidence = m.Groups["conf"].Success ? decimal.Parse(m.Groups["conf"].Value, CultureInfo.InvariantCulture) / 100m : 0.97m;
            fields.Add(new OcrFieldReading(label, raw, Normalise(label, raw), confidence));
        }

        if (fields.Count == 0)
        {
            throw new InvalidOperationException("No readable fields were found in the document.");
        }

        return new PodOcrExtraction(Provider, fields, Math.Round(fields.Average(f => f.Confidence), 4), null);
    }

    private static string Normalise(string label, string raw) => label switch
    {
        OcrReconciler.ShipmentNumber or OcrReconciler.DeliveryNumber or OcrReconciler.InvoiceNumber or OcrReconciler.VehicleNumber => OcrReconciler.Code(raw),
        OcrReconciler.DeliveredQuantity or OcrReconciler.ShortQuantity => raw.Replace(",", string.Empty, StringComparison.Ordinal),
        _ => raw,
    };

    private static string Unescape(string s) => s.Replace("\\(", "(", StringComparison.Ordinal).Replace("\\)", ")", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\)")]
    private static partial Regex PdfString();

    [GeneratedRegex(@"^(?<label>[A-Za-z ]+?)\s*[:=]\s*(?<value>.*?)(?:\s*\[(?<conf>\d{1,3})%\])?$")]
    private static partial Regex Line();
}

internal sealed record PodOcrJob(Guid TenantId, Guid? UserId, Guid OcrResultId);

/// <summary>Work waiting to be read. In-process; one that is lost on restart stays Queued and is requeued by asking for OCR again.</summary>
internal interface IPodOcrQueue
{
    ValueTask EnqueueAsync(PodOcrJob job, CancellationToken cancellationToken);

    IAsyncEnumerable<PodOcrJob> ReadAllAsync(CancellationToken cancellationToken);
}

internal sealed class PodOcrQueue : IPodOcrQueue
{
    private readonly Channel<PodOcrJob> _channel = Channel.CreateUnbounded<PodOcrJob>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(PodOcrJob job, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<PodOcrJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Reads one queued document, reconciles it with the delivery and decides what happens to the proof. Runs as the tenant that asked.</summary>
internal sealed class PodOcrProcessor(
    DeliveriesDbContext db, IFileStore files, IPodOcrService ocr, PodEngine engine, TimeProvider clock, ILogger<PodOcrProcessor> logger)
{
    public async Task ProcessAsync(Guid ocrResultId, CancellationToken cancellationToken)
    {
        var result = await db.OcrResults.Include(o => o.Fields).FirstOrDefaultAsync(o => o.Id == ocrResultId, cancellationToken);
        if (result is null || result.ProcessingStatus is OcrStatus.Completed)
        {
            return;
        }

        var pod = await db.Pods.Include(p => p.Items).Include(p => p.Evidence).Include(p => p.Signatures).Include(p => p.Validations).Include(p => p.Reviews)
            .Include(p => p.OcrResults).ThenInclude(o => o.Fields).AsSplitQuery().FirstAsync(p => p.Id == result.PodId, cancellationToken);
        var delivery = await db.Deliveries.Include(d => d.Items).Include(d => d.Discrepancies).AsSplitQuery().FirstAsync(d => d.Id == pod.DeliveryId, cancellationToken);
        var live = pod.OcrResults.First(o => o.Id == ocrResultId);
        var evidence = pod.Evidence.FirstOrDefault(e => e.Id == live.EvidenceId);
        var now = clock.GetUtcNow();

        live.Start();
        try
        {
            if (evidence is null)
            {
                throw new InvalidOperationException("The document is no longer on the proof.");
            }

            await using var stream = await files.OpenReadAsync(evidence.FileKey, cancellationToken) ?? throw new InvalidOperationException("The document file is missing.");
            var extraction = await ocr.ExtractAsync(new OcrDocument(evidence.FileName, evidence.ContentType, stream), cancellationToken);
            live.Complete(extraction.Provider, extraction.Fields.Select(f => (f.Name, f.Raw, f.Normalized, f.Confidence, f.BoundingBoxJson)), extraction.OverallConfidence, null, now);
            logger.LogInformation("Read POD {Pod}: {Fields} field(s) by {Provider}", pod.PodNumber, extraction.Fields.Count, extraction.Provider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            live.Fail(ex.Message, now);
            logger.LogWarning("Could not read POD {Pod}: {Reason}", pod.PodNumber, ex.Message);
        }

        // Whatever the outcome, the proof now moves on: to acceptance if the paper agrees, otherwise to a person.
        await engine.DecideAsync(pod, delivery, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Reads queued documents one at a time, as the tenant and user who submitted them, so submitting a proof never waits on slow OCR.</summary>
internal sealed class PodOcrWorker(IServiceScopeFactory scopes, IPodOcrQueue queue, ILogger<PodOcrWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<IAmbientUserContext>().RunAs(job.TenantId, job.UserId, $"ocr:{job.OcrResultId:N}");
                    await scope.ServiceProvider.GetRequiredService<PodOcrProcessor>().ProcessAsync(job.OcrResultId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Reading document {Job} failed", job.OcrResultId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
