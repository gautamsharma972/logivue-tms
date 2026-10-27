using System.Security.Cryptography;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record StoredBlob(string Key, string ContentType, long SizeBytes, string Sha256, string SafeName);

/// <summary>
/// The one way a file enters the system: its size is capped, its type is decided by its first bytes (never its name or declared type), it is scanned, and it is stored under a key the
/// system chooses. Used for request attachments and every POD upload.
/// </summary>
internal sealed class FileIntake(IFileStore store, IFileScanner scanner, ITenantDirectory tenants, TimeProvider clock)
{
    private readonly Dictionary<Guid, string> _codes = [];

    /// <summary>
    /// Delivery evidence (LR invoice copies and PODs) is filed per organisation, month and LR:
    /// <c>delivery-evidence/{tenantCode}/{yyyy}/{MM}/{lrNo}/{lrNo}-{INV|POD}-{nn}{ext}</c>. Anything not tied to an LR keeps the <c>ops/{folder}</c> layout.
    /// </summary>
    public Task<Result<StoredBlob>> SaveEvidenceAsync(Stream content, string? originalName, Guid tenantId, string lrNumber, string kind, int sequence, long maxBytes, CancellationToken cancellationToken) =>
        SaveAsync(content, originalName, "evidence", maxBytes, cancellationToken, (tenantId, lrNumber, kind, sequence));

    public async Task<Result<StoredBlob>> SaveAsync(Stream content, string? originalName, string folder, long maxBytes, CancellationToken cancellationToken, (Guid TenantId, string LrNumber, string Kind, int Sequence)? evidence = null)
    {
        var limit = Math.Min(maxBytes, FileSniffer.MaxBytes);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                return Error.Validation("ops.file_too_large", $"The file is larger than {limit / (1024 * 1024)} MB.");
            }
        }

        if (buffer.Length == 0)
        {
            return Error.Validation("ops.file_empty", "The file is empty.");
        }

        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        if (FileSniffer.Identify(bytes.Span) is not { } kind)
        {
            return Error.Validation("ops.file_type", "Only PDF, JPEG and PNG files are accepted.");
        }

        var scan = await scanner.ScanAsync(bytes, kind.ContentType, cancellationToken);
        if (!scan.IsClean)
        {
            return Error.Validation("ops.file_rejected", scan.Detail ?? "The file was refused by the security scan.");
        }

        var now = clock.GetUtcNow();
        var key = evidence is { } e
            ? $"delivery-evidence/{await TenantFolderAsync(e.TenantId, cancellationToken)}/{now:yyyy}/{now:MM}/{PathSafe(e.LrNumber, "lr")}/{PathSafe(e.LrNumber, "lr")}-{e.Kind}-{e.Sequence:D2}{kind.Extension}"
            : $"ops/{folder}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{kind.Extension}";
        await store.SaveAsync(key, new MemoryStream(buffer.GetBuffer(), 0, (int)buffer.Length, writable: false), cancellationToken);
        return new StoredBlob(key, kind.ContentType, buffer.Length, Convert.ToHexStringLower(SHA256.HashData(bytes.Span)), SafeName(originalName, kind.Extension));
    }

    /// <summary>The organisation code made safe for a storage path; falls back to the tenant id if the code is unknown.</summary>
    private async Task<string> TenantFolderAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!_codes.TryGetValue(tenantId, out var folder))
        {
            var code = await tenants.GetCodeAsync(tenantId, cancellationToken);
            folder = PathSafe(code, tenantId.ToString("N"));
            _codes[tenantId] = folder;
        }

        return folder;
    }

    private static string PathSafe(string? value, string fallback)
    {
        var clean = new string((value ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length == 0 ? fallback : clean;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => store.DeleteAsync(key, cancellationToken);

    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken) => store.OpenReadAsync(key, cancellationToken);

    private static string SafeName(string? name, string extension)
    {
        var file = Path.GetFileNameWithoutExtension(name ?? string.Empty);
        var clean = new string(file.Where(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '"' or '<' or '>' or '|' or '?' or '*')).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "file";
        }

        return (clean.Length > 120 ? clean[..120] : clean) + extension;
    }
}
