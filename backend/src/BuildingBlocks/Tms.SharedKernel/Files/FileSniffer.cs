namespace Tms.SharedKernel.Files;

/// <summary>
/// Identifies uploaded documents by their leading bytes. The client-supplied content type and file name are never
/// trusted: a renamed executable must not be stored and served back as a "certificate".
/// </summary>
public static class FileSniffer
{
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <returns>The real content type and a safe extension for PDF / JPEG / PNG, or null for anything else.</returns>
    public static (string ContentType, string Extension)? Identify(ReadOnlySpan<byte> head) => head switch
    {
        [0x25, 0x50, 0x44, 0x46, ..] => ("application/pdf", ".pdf"),
        [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", ".jpg"),
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ("image/png", ".png"),
        _ => null,
    };
}
