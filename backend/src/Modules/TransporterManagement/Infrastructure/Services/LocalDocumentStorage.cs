using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>
/// Stores documents on the local file system under a configured root. Stored names are generated, so the
/// original file name never reaches the disk path, and references are resolved only inside the root.
/// Swap for object storage behind <see cref="IDocumentStorage"/> when it is available.
/// </summary>
internal sealed class LocalDocumentStorage(string rootDirectory) : IDocumentStorage
{
    private readonly string _root = Path.GetFullPath(rootDirectory);

    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var reference = $"{Guid.NewGuid():N}{extension}";
        var path = Resolve(reference);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);

        return new StoredFile(reference, file.Length);
    }

    public Task<Stream> OpenReadAsync(string fileReference, CancellationToken cancellationToken = default)
    {
        var path = Resolve(fileReference);
        if (!File.Exists(path))
        {
            throw new NotFoundException("The document file is missing from storage.", "DOCUMENT_FILE_NOT_FOUND");
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string fileReference, CancellationToken cancellationToken = default)
    {
        var path = Resolve(fileReference);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Contains(Path.DirectorySeparatorChar) || reference.Contains(Path.AltDirectorySeparatorChar) || reference.Contains(".."))
        {
            throw new ArgumentException("Invalid document reference.", nameof(reference));
        }

        return Path.Combine(_root, reference);
    }
}
