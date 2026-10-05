namespace Tms.SharedKernel.Files;

/// <param name="IsClean">False when the file must be refused.</param>
/// <param name="Detail">Why it was refused (shown to the uploader), or how it was scanned.</param>
public sealed record FileScanResult(bool IsClean, string? Detail = null)
{
    public static FileScanResult Clean { get; } = new(true);
}

/// <summary>
/// Extension point for malware scanning of uploaded files. Called after the file has been identified by its bytes and before it is stored. The default does nothing;
/// register a ClamAV, cloud or gateway implementation to turn scanning on. A scanner that cannot answer should throw, so the upload fails rather than passing unscanned.
/// </summary>
public interface IFileScanner
{
    Task<FileScanResult> ScanAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);
}

/// <summary>Scans nothing. It is the default until a real scanner is registered.</summary>
public sealed class NoFileScanner : IFileScanner
{
    public Task<FileScanResult> ScanAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) => Task.FromResult(FileScanResult.Clean);
}
