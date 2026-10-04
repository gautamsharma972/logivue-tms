using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Documents;

/// <summary>Form fields of a document upload (multipart).</summary>
public sealed class UploadDocumentForm
{
    public OwnerKind OwnerKind { get; init; }

    public Guid OwnerId { get; init; }

    public DocumentKind Kind { get; init; }

    public string? Number { get; init; }

    public DateOnly? IssuedOn { get; init; }

    public DateOnly? ExpiresOn { get; init; }

    public IFormFile? File { get; init; }
}

internal sealed record DownloadedFile(Stream Content, string FileName, string ContentType);

internal sealed class DocumentHandler(
    TransportersDbContext db,
    ICurrentUser currentUser,
    TransporterAccess access,
    IFileStore files,
    TimeProvider clock,
    Application.MasterData.DocumentPolicyProvider policies)
{
    public async Task<Result<IReadOnlyList<DocumentDto>>> ListAsync(Guid transporterId, OwnerKind? ownerKind, Guid? ownerId, bool includeSuperseded, CancellationToken cancellationToken)
    {
        var allowed = access.Check(transporterId, AccessLevel.Read);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var documents = db.Documents.AsNoTracking().Where(d => d.TransporterId == transporterId);
        if (ownerKind is { } kind)
        {
            documents = documents.Where(d => d.OwnerKind == kind);
        }

        if (ownerId is { } owner)
        {
            documents = documents.Where(d => d.OwnerId == owner);
        }

        if (!includeSuperseded)
        {
            documents = documents.Where(d => d.SupersededAt == null);
        }

        var today = clock.TodayInIndia();
        var rows = await documents.OrderBy(d => d.OwnerKind).ThenBy(d => d.Kind).ThenByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        var policy = await policies.GetAsync(cancellationToken);
        return Result.Success<IReadOnlyList<DocumentDto>>(rows.Select(d => d.ToDto(today, policy)).ToList());
    }

    public async Task<Result<DocumentDto>> UploadAsync(Guid transporterId, UploadDocumentForm form, CancellationToken cancellationToken)
    {
        var allowed = access.Check(transporterId, AccessLevel.Manage);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (form.File is not { Length: > 0 } file)
        {
            return Error.Validation("documents.file_required", "Attach a file.");
        }

        if (file.Length > FileSniffer.MaxBytes)
        {
            return Error.Validation("documents.file_too_large", "Files can be at most 10 MB.");
        }

        // The owner must exist, belong to this transporter, and (for vendors) be the vendor's own.
        if (!await OwnerBelongsToAsync(form.OwnerKind, form.OwnerId, transporterId, cancellationToken))
        {
            return Error.NotFound("documents.owner_not_found", "The record this document belongs to was not found.");
        }

        var head = new byte[8];
        await using var upload = file.OpenReadStream();
        var read = await upload.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (FileSniffer.Identify(head.AsSpan(0, read)) is not var (contentType, extension))
        {
            return Error.Validation("documents.file_type", "Upload a PDF, JPG or PNG file.");
        }

        upload.Position = 0;
        var tenantId = currentUser.TenantId!.Value;
        var key = $"{tenantId}/{transporterId}/{Guid.CreateVersion7()}{extension}";
        var fileName = Path.GetFileName(file.FileName);

        var created = ComplianceDocument.Create(
            tenantId, transporterId, form.OwnerKind, form.OwnerId, form.Kind, form.Number, form.IssuedOn, form.ExpiresOn,
            key, string.IsNullOrWhiteSpace(fileName) ? $"document{extension}" : fileName[..Math.Min(fileName.Length, 255)], contentType, file.Length,
            await policies.GetAsync(cancellationToken));
        if (created.IsFailure)
        {
            return created.Error;
        }

        // Replace, don't pile up: older papers of the same kind stay for history but stop counting as current.
        var now = clock.GetUtcNow();
        var previous = await db.Documents
            .Where(d => d.OwnerKind == form.OwnerKind && d.OwnerId == form.OwnerId && d.Kind == form.Kind && d.SupersededAt == null)
            .ToListAsync(cancellationToken);
        previous.ForEach(d => d.Supersede(now));

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

        return created.Value.ToDto(clock.TodayInIndia(), await policies.GetAsync(cancellationToken));
    }

    public async Task<Result<DownloadedFile>> DownloadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null || access.Check(document.TransporterId, AccessLevel.Read).IsFailure)
        {
            return Error.NotFound("documents.not_found", "Document not found.");
        }

        var stream = await files.OpenReadAsync(document.FileKey, cancellationToken);
        return stream is null
            ? Error.NotFound("documents.file_missing", "The stored file could not be found.")
            : new DownloadedFile(stream, document.FileName, document.ContentType);
    }

    public async Task<Result> DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null || access.Check(document.TransporterId, AccessLevel.Manage).IsFailure)
        {
            return Error.NotFound("documents.not_found", "Document not found.");
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(document.FileKey, cancellationToken);
        return Result.Success();
    }

    private async Task<bool> OwnerBelongsToAsync(OwnerKind kind, Guid ownerId, Guid transporterId, CancellationToken cancellationToken) => kind switch
    {
        OwnerKind.Transporter => ownerId == transporterId && await db.Transporters.AnyAsync(t => t.Id == ownerId, cancellationToken),
        OwnerKind.Vehicle => await db.Vehicles.AnyAsync(v => v.Id == ownerId && v.TransporterId == transporterId, cancellationToken),
        OwnerKind.Driver => await db.Drivers.AnyAsync(d => d.Id == ownerId && d.TransporterId == transporterId, cancellationToken),
        _ => false,
    };
}

/// <summary>Everything that has expired or expires soon, oldest first — the worklist for chasing renewals.</summary>
internal sealed class ComplianceReportHandler(TransportersDbContext db, TransporterAccess access, TimeProvider clock, Application.MasterData.DocumentPolicyProvider policies)
{
    public async Task<Result<PagedResult<ComplianceItemDto>>> HandleAsync(ComplianceReportQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanUseModule)
        {
            return TransporterAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var horizon = today.AddDays(Math.Clamp(query.WithinDays, 0, 365));

        var documents = db.Documents.AsNoTracking().Where(d => d.SupersededAt == null && d.ExpiresOn != null && d.ExpiresOn <= horizon);
        if (access.OwnTransporterId is { } own)
        {
            documents = documents.Where(d => d.TransporterId == own);
        }

        var page = await documents.OrderBy(d => d.ExpiresOn).ThenBy(d => d.Id).ToPagedAsync(query.Page, query.PageSize, cancellationToken);

        var vehicleIds = page.Items.Where(d => d.OwnerKind == OwnerKind.Vehicle).Select(d => d.OwnerId).ToList();
        var driverIds = page.Items.Where(d => d.OwnerKind == OwnerKind.Driver).Select(d => d.OwnerId).ToList();
        var transporterIds = page.Items.Select(d => d.TransporterId).Distinct().ToList();

        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.RegistrationNumber, cancellationToken);
        var drivers = await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.FullName, cancellationToken);
        var transporters = await db.Transporters.AsNoTracking().Where(t => transporterIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken);

        var policy = await policies.GetAsync(cancellationToken);
        var items = page.Items.Select(d => new ComplianceItemDto(
            d.ToDto(today, policy),
            d.OwnerKind switch
            {
                OwnerKind.Vehicle => vehicles.GetValueOrDefault(d.OwnerId, "Vehicle"),
                OwnerKind.Driver => drivers.GetValueOrDefault(d.OwnerId, "Driver"),
                _ => "Company",
            },
            transporters.GetValueOrDefault(d.TransporterId, "Unknown"))).ToList();

        return new PagedResult<ComplianceItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}
