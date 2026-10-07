using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Contracts;

/// <summary>Form fields of a contract-document upload (multipart).</summary>
public sealed class UploadContractDocumentForm
{
    public ContractDocumentKind Kind { get; init; }

    public string? Title { get; init; }

    public IFormFile? File { get; init; }

    /// <summary>The document's own number. Uploading another file with the same kind and number makes the next version; the earlier file is kept.</summary>
    public string? Number { get; init; }

    public DateOnly? IssueDate { get; init; }

    public DateOnly? EffectiveDate { get; init; }

    public DateOnly? ExpiryDate { get; init; }
}

internal sealed record DownloadedContractFile(Stream Content, string FileName, string ContentType);

/// <summary>The contract's document repository: signed agreement, annexures, amendments.</summary>
internal sealed class DocumentHandler(ContractsDbContext db, ContractAccess access, ICurrentUser currentUser, IFileStore files, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<ContractDocumentDto>>> ListAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        if (!await db.Contracts.AnyAsync(c => c.Id == contractId, cancellationToken))
        {
            return ContractAccess.NotFound;
        }

        var documents = await db.Documents.AsNoTracking().Where(d => d.ContractId == contractId).OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ContractDocumentDto>>(documents.Select(d => d.ToDto()).ToList());
    }

    public async Task<Result<ContractDocumentDto>> UploadAsync(Guid contractId, UploadContractDocumentForm form, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        if (!await db.Contracts.AnyAsync(c => c.Id == contractId, cancellationToken))
        {
            return ContractAccess.NotFound;
        }

        if (form.File is not { Length: > 0 } file)
        {
            return Error.Validation("contract_documents.file_required", "Attach a file.");
        }

        if (file.Length > FileSniffer.MaxBytes)
        {
            return Error.Validation("contract_documents.file_too_large", "Files can be at most 10 MB.");
        }

        var head = new byte[8];
        await using var upload = file.OpenReadStream();
        var read = await upload.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (FileSniffer.Identify(head.AsSpan(0, read)) is not var (contentType, extension))
        {
            return Error.Validation("contract_documents.file_type", "Upload a PDF, JPG or PNG file.");
        }

        upload.Position = 0;
        var tenantId = currentUser.TenantId!.Value;
        var number = string.IsNullOrWhiteSpace(form.Number) ? null : form.Number.Trim();
        var version = 1;
        if (number is not null)
        {
            var kind = form.Kind;
            version = (await db.Documents.AsNoTracking().Where(d => d.ContractId == contractId && d.Kind == kind && d.Number == number).MaxAsync(d => (int?)d.DocumentVersion, cancellationToken) ?? 0) + 1;
        }

        var documentId = Guid.CreateVersion7();
        var key = $"{tenantId}/contracts/{contractId}/{version}/{documentId}{extension}";
        var fileName = Path.GetFileName(file.FileName);

        var created = ContractDocument.Create(tenantId, contractId, form.Kind, form.Title ?? string.Empty, key,
            string.IsNullOrWhiteSpace(fileName) ? $"document{extension}" : fileName[..Math.Min(fileName.Length, 255)], contentType, file.Length);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var described = created.Value.Describe(number, version, form.IssueDate, form.EffectiveDate, form.ExpiryDate);
        if (described.IsFailure)
        {
            return described.Error;
        }

        await files.SaveAsync(key, upload, cancellationToken);
        try
        {
            db.Documents.Add(created.Value);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(key, CancellationToken.None); // don't leave an orphaned blob behind
            throw;
        }

        return created.Value.ToDto();
    }

    public async Task<Result<DownloadedContractFile>> DownloadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("contract_documents.not_found", "Document not found.");
        }

        var stream = await files.OpenReadAsync(document.FileKey, cancellationToken);
        return stream is null
            ? Error.NotFound("contract_documents.file_missing", "The stored file could not be found.")
            : new DownloadedContractFile(stream, document.FileName, document.ContentType);
    }

    public async Task<Result<ContractDocumentDto>> VerifyAsync(Guid contractId, Guid documentId, CancellationToken cancellationToken)
    {
        if (!access.CanVerify)
        {
            return ContractAccess.Forbidden;
        }

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.ContractId == contractId, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("contract_documents.not_found", "Document not found.");
        }

        var verified = document.Verify(currentUser.UserId, clock.GetUtcNow());
        if (verified.IsFailure)
        {
            return verified.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return document.ToDto();
    }

    public async Task<Result> DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return Error.NotFound("contract_documents.not_found", "Document not found.");
        }

        if (document.Status == DocumentStatus.Verified)
        {
            return Error.Conflict("contract_documents.verified", "A verified contract document cannot be deleted. Upload a new version instead.");
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(document.FileKey, cancellationToken);
        return Result.Success();
    }
}
