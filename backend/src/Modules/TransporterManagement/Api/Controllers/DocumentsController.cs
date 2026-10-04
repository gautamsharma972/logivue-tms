using LogiVue.Tms.TransporterManagement.Application.Documents;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Multipart form for uploading a document with its metadata.</summary>
public sealed class DocumentUploadForm
{
    public IFormFile? File { get; set; }

    public long DocumentTypeId { get; set; }

    public string? DocumentNumber { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public long? VehicleId { get; set; }

    public long? DriverId { get; set; }

    public string? Remarks { get; set; }
}

/// <summary>Transporter and vehicle compliance documents: upload, metadata, verification and download.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/documents")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class DocumentsController(IDocumentService documents) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentDto>>> List(
        long transporterId,
        [FromQuery] long? vehicleId,
        [FromQuery] DocumentVerificationStatus? verificationStatus,
        CancellationToken cancellationToken) =>
        Ok(await documents.ListAsync(transporterId, vehicleId, verificationStatus, cancellationToken));

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentRules.MaxFileSizeBytes + 1024 * 1024)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.DocumentEditors)]
    public async Task<ActionResult<DocumentDto>> Upload(long transporterId, [FromForm] DocumentUploadForm form, CancellationToken cancellationToken)
    {
        var details = new SaveDocumentRequest(form.DocumentTypeId, form.DocumentNumber, form.IssueDate, form.ExpiryDate, form.VehicleId, form.Remarks, form.DriverId);

        // A missing file becomes an empty upload, so the validator reports it alongside any metadata errors.
        if (form.File is null)
        {
            var empty = new DocumentUpload(details, string.Empty, string.Empty, 0, Stream.Null);
            return StatusCode(StatusCodes.Status201Created, await documents.UploadAsync(transporterId, empty, cancellationToken));
        }

        await using var content = form.File.OpenReadStream();
        var upload = new DocumentUpload(details, form.File.FileName, form.File.ContentType, form.File.Length, content);
        var created = await documents.UploadAsync(transporterId, upload, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{documentId:long}")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.DocumentEditors)]
    public async Task<ActionResult<DocumentDto>> Update(long transporterId, long documentId, [FromBody] SaveDocumentRequest request, CancellationToken cancellationToken) =>
        Ok(await documents.UpdateAsync(transporterId, documentId, request, cancellationToken));

    [HttpPost("{documentId:long}/verify")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = AccessRoles.Compliance)]
    public async Task<ActionResult<DocumentDto>> Verify(long transporterId, long documentId, [FromBody] VerifyDocumentRequest? request, CancellationToken cancellationToken) =>
        Ok(await documents.VerifyAsync(transporterId, documentId, request ?? new VerifyDocumentRequest(null), cancellationToken));

    [HttpPost("{documentId:long}/reject")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = AccessRoles.Compliance)]
    public async Task<ActionResult<DocumentDto>> Reject(long transporterId, long documentId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await documents.RejectAsync(transporterId, documentId, request, cancellationToken));

    [HttpGet("{documentId:long}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(long transporterId, long documentId, CancellationToken cancellationToken)
    {
        var file = await documents.GetFileAsync(transporterId, documentId, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
