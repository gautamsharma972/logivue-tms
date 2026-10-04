using System.IO;
using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Documents;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentDto>> ListAsync(long transporterId, long? vehicleId, DocumentVerificationStatus? status, CancellationToken cancellationToken = default);

    Task<DocumentDto> UploadAsync(long transporterId, DocumentUpload upload, CancellationToken cancellationToken = default);

    Task<DocumentDto> UpdateAsync(long transporterId, long documentId, SaveDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDto> VerifyAsync(long transporterId, long documentId, VerifyDocumentRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDto> RejectAsync(long transporterId, long documentId, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<DocumentFile> GetFileAsync(long transporterId, long documentId, CancellationToken cancellationToken = default);
}

public sealed class DocumentService(
    IRepository<Transporter> transporters,
    IRepository<DocumentType> documentTypes,
    IRepository<TransporterVehicle> vehicles,
    IRepository<TransporterDriver> drivers,
    IRepository<TransporterDocument> documents,
    IDocumentStorage storage,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<SaveDocumentRequest> metadataValidator,
    IValidator<DocumentUpload> uploadValidator,
    IValidator<ReasonRequest> reasonValidator,
    ILogger<DocumentService> logger) : IDocumentService
{
    public async Task<IReadOnlyList<DocumentDto>> ListAsync(long transporterId, long? vehicleId, DocumentVerificationStatus? status, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);

        var docs = await documents.ListAsync(d =>
            d.TransporterId == transporterId
            && (vehicleId == null || d.VehicleId == vehicleId)
            && (status == null || d.VerificationStatus == status), cancellationToken);

        var types = await documentTypes.ListAsync(_ => true, cancellationToken);
        var byId = types.ToDictionary(t => t.Id);

        return docs.OrderByDescending(d => d.Id)
            .Select(d => ToDto(d, byId[d.DocumentTypeId]))
            .ToList();
    }

    public async Task<DocumentDto> UploadAsync(long transporterId, DocumentUpload upload, CancellationToken cancellationToken = default)
    {
        await metadataValidator.ValidateAndThrowAsync(upload.Details, cancellationToken);
        await uploadValidator.ValidateAndThrowAsync(upload, cancellationToken);

        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var type = await ResolveTypeAsync(upload.Details, transporterId, cancellationToken);

        var stored = await storage.SaveAsync(upload.Content, upload.OriginalFileName, cancellationToken);
        TransporterDocument entity;
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            entity = new TransporterDocument
            {
                TransporterId = transporterId,
                VehicleId = upload.Details.VehicleId,
                DriverId = upload.Details.DriverId,
                DocumentTypeId = type.Id,
                DocumentNumber = Clean(upload.Details.DocumentNumber),
                IssueDate = upload.Details.IssueDate,
                ExpiryDate = upload.Details.ExpiryDate,
                FileReference = stored.FileReference,
                OriginalFileName = upload.OriginalFileName,
                ContentType = upload.ContentType,
                FileSizeBytes = stored.SizeBytes,
                Remarks = Clean(upload.Details.Remarks),
                VerificationStatus = type.VerificationRequired ? DocumentVerificationStatus.Pending : DocumentVerificationStatus.Verified,
                VerifiedBy = type.VerificationRequired ? null : currentUser.UserId,
                VerifiedAt = type.VerificationRequired ? null : now
            };

            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

            documents.Add(entity);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await audit.RecordAsync(new AuditEntry("TransporterDocument", entity.Id.ToString(), "DocumentUploaded",
                NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Without a committed record the stored file is unreachable, so it is removed.
            await storage.DeleteAsync(stored.FileReference, CancellationToken.None);
            throw;
        }

        logger.LogInformation("Document {DocumentTypeCode} uploaded for transporter {TransporterId}", type.Code, transporterId);

        return ToDto(entity, type);
    }

    public async Task<DocumentDto> UpdateAsync(long transporterId, long documentId, SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        await metadataValidator.ValidateAndThrowAsync(request, cancellationToken);

        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var entity = await LoadDocumentAsync(transporterId, documentId, cancellationToken);
        var type = await ResolveTypeAsync(request, transporterId, cancellationToken);

        var before = AuditJson.Serialize(entity);

        var identityChanged = entity.DocumentTypeId != type.Id
            || entity.VehicleId != request.VehicleId
            || entity.DriverId != request.DriverId
            || entity.DocumentNumber != Clean(request.DocumentNumber)
            || entity.IssueDate != request.IssueDate
            || entity.ExpiryDate != request.ExpiryDate;

        entity.DocumentTypeId = type.Id;
        entity.VehicleId = request.VehicleId;
        entity.DriverId = request.DriverId;
        entity.DocumentNumber = Clean(request.DocumentNumber);
        entity.IssueDate = request.IssueDate;
        entity.ExpiryDate = request.ExpiryDate;
        entity.Remarks = Clean(request.Remarks);

        if (identityChanged && type.VerificationRequired)
        {
            // Verification applies to the document as it was checked. Changing its identity requires checking it again.
            entity.VerificationStatus = DocumentVerificationStatus.Pending;
            entity.VerifiedBy = null;
            entity.VerifiedAt = null;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterDocument", documentId.ToString(), "DocumentUpdated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(entity, type);
    }

    public async Task<DocumentDto> VerifyAsync(long transporterId, long documentId, VerifyDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        EnsureVerifier();

        var entity = await LoadDocumentAsync(transporterId, documentId, cancellationToken);
        if (entity.VerificationStatus == DocumentVerificationStatus.Verified)
        {
            throw new BusinessRuleException("The document is already verified.", "DOCUMENT_ALREADY_VERIFIED");
        }

        var before = AuditJson.Serialize(entity);
        entity.VerificationStatus = DocumentVerificationStatus.Verified;
        entity.VerifiedBy = currentUser.UserId;
        entity.VerifiedAt = clock.GetUtcNow().UtcDateTime;
        entity.Remarks = Clean(request.Remarks) ?? entity.Remarks;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterDocument", documentId.ToString(), "DocumentVerified",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity), Reason: Clean(request.Remarks)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(entity, await TypeAsync(entity.DocumentTypeId, cancellationToken));
    }

    public async Task<DocumentDto> RejectAsync(long transporterId, long documentId, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var reason = request.Reason;

        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        EnsureVerifier();

        var entity = await LoadDocumentAsync(transporterId, documentId, cancellationToken);
        var before = AuditJson.Serialize(entity);

        entity.VerificationStatus = DocumentVerificationStatus.Rejected;
        entity.VerifiedBy = currentUser.UserId;
        entity.VerifiedAt = clock.GetUtcNow().UtcDateTime;
        entity.Remarks = reason.Trim();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterDocument", documentId.ToString(), "DocumentRejected",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity), Reason: reason.Trim()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(entity, await TypeAsync(entity.DocumentTypeId, cancellationToken));
    }

    public async Task<DocumentFile> GetFileAsync(long transporterId, long documentId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        var entity = await LoadDocumentAsync(transporterId, documentId, cancellationToken);

        if (string.IsNullOrWhiteSpace(entity.FileReference))
        {
            throw new NotFoundException($"Document {documentId} has no file attached.", "DOCUMENT_FILE_NOT_FOUND");
        }

        var content = await storage.OpenReadAsync(entity.FileReference, cancellationToken);
        return new DocumentFile(content, entity.ContentType ?? "application/octet-stream", entity.OriginalFileName ?? $"document-{documentId}");
    }

    /// <summary>Verification is performed by compliance staff, transport managers or administrators.</summary>
    private void EnsureVerifier()
    {
        if (!currentUser.IsInRole(Roles.ComplianceUser) && !currentUser.IsInRole(Roles.TransportManager) && !currentUser.IsInRole(Roles.TransportAdmin))
        {
            throw new ForbiddenException("Only compliance users can verify or reject documents.", "WORKFLOW_ROLE_REQUIRED");
        }
    }

    /// <summary>Checks the document type exists and is active, and that vehicle scope matches the type.</summary>
    private async Task<DocumentType> ResolveTypeAsync(SaveDocumentRequest request, long transporterId, CancellationToken cancellationToken)
    {
        var type = await documentTypes.FindAsync(request.DocumentTypeId, cancellationToken);
        if (type is null || !type.IsActive)
        {
            throw new BusinessRuleException($"Document type {request.DocumentTypeId} is not available.", "DOCUMENT_TYPE_INVALID");
        }

        if (request.VehicleId is not null && request.DriverId is not null)
        {
            throw new BusinessRuleException("A document is attached to a vehicle or a driver, not both.", "DOCUMENT_SCOPE_INVALID");
        }

        if (request.DriverId is { } driverId)
        {
            var driver = await drivers.FindAsync(driverId, cancellationToken);
            if (driver is null || driver.TransporterId != transporterId)
            {
                throw new NotFoundException($"Driver {driverId} was not found for transporter {transporterId}.", "DRIVER_NOT_FOUND");
            }

            if (!type.IsDriverLevel)
            {
                throw new BusinessRuleException($"{type.Name} is not a driver document and cannot be attached to a driver.", "DOCUMENT_SCOPE_INVALID");
            }
        }
        else if (request.VehicleId is { } vehicleId)
        {
            var vehicle = await vehicles.FindAsync(vehicleId, cancellationToken);
            if (vehicle is null || vehicle.TransporterId != transporterId)
            {
                throw new NotFoundException($"Vehicle {vehicleId} was not found for transporter {transporterId}.", "VEHICLE_NOT_FOUND");
            }

            if (!type.IsVehicleLevel)
            {
                throw new BusinessRuleException($"{type.Name} is a transporter-level document and cannot be attached to a vehicle.", "DOCUMENT_SCOPE_INVALID");
            }
        }
        else if (!type.IsTransporterLevel)
        {
            throw new BusinessRuleException(
                type.IsDriverLevel ? $"{type.Name} must be attached to a driver." : $"{type.Name} must be attached to a vehicle.",
                type.IsDriverLevel ? "DOCUMENT_DRIVER_REQUIRED" : "DOCUMENT_VEHICLE_REQUIRED");
        }

        if (type.ExpiryRequired && request.ExpiryDate is null)
        {
            throw new BusinessRuleException($"{type.Name} requires an expiry date.", "DOCUMENT_EXPIRY_REQUIRED");
        }

        return type;
    }

    private async Task<TransporterDocument> LoadDocumentAsync(long transporterId, long documentId, CancellationToken cancellationToken)
    {
        var entity = await documents.FindAsync(documentId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Document {documentId} was not found for transporter {transporterId}.", "DOCUMENT_NOT_FOUND");
        }

        return entity;
    }

    private async Task<DocumentType> TypeAsync(long typeId, CancellationToken cancellationToken) =>
        await documentTypes.FindAsync(typeId, cancellationToken) ?? throw new NotFoundException($"Document type {typeId} was not found.", "DOCUMENT_TYPE_INVALID");

    private static DocumentDto ToDto(TransporterDocument d, DocumentType t) =>
        new(d.Id, d.TransporterId, d.VehicleId, d.DocumentTypeId, t.Code, t.Name, d.DocumentNumber, d.IssueDate, d.ExpiryDate,
            d.OriginalFileName, d.ContentType, d.FileSizeBytes, d.VerificationStatus, d.VerifiedBy, d.VerifiedAt, d.Remarks, d.DriverId);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
