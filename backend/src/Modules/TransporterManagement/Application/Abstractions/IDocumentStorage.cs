namespace LogiVue.Tms.TransporterManagement.Application.Abstractions;

/// <summary>
/// Stores uploaded compliance documents outside the database. Implementations generate the stored name;
/// callers only ever receive and pass back the opaque <see cref="StoredFile.FileReference"/>.
/// </summary>
public interface IDocumentStorage
{
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string fileReference, CancellationToken cancellationToken = default);

    /// <summary>Removes a stored file. Used to undo a save whose database change did not commit.</summary>
    Task DeleteAsync(string fileReference, CancellationToken cancellationToken = default);
}

public sealed record StoredFile(string FileReference, long SizeBytes);
