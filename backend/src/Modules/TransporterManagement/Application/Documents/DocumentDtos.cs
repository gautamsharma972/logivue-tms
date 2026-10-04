using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Documents;

public sealed record DocumentDto(
    long Id,
    long TransporterId,
    long? VehicleId,
    long DocumentTypeId,
    string DocumentTypeCode,
    string DocumentTypeName,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? OriginalFileName,
    string? ContentType,
    long? FileSizeBytes,
    DocumentVerificationStatus VerificationStatus,
    string? VerifiedBy,
    DateTime? VerifiedAt,
    string? Remarks,
    long? DriverId = null);

/// <summary>Metadata for a document. Changing identifying fields after verification sends it back for verification.</summary>
public sealed record SaveDocumentRequest(
    long DocumentTypeId,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    long? VehicleId,
    string? Remarks,
    long? DriverId = null);

/// <summary>An uploaded file with its metadata. <see cref="Content"/> is read once, by the storage implementation.</summary>
public sealed record DocumentUpload(
    SaveDocumentRequest Details,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

public sealed record DocumentFile(Stream Content, string ContentType, string FileName);

public sealed record VerifyDocumentRequest(string? Remarks);
