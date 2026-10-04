using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Claims;
using LogiVue.Tms.TransporterManagement.Application.Documents;
using LogiVue.Tms.TransporterManagement.Application.Fleet;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Pod;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>
/// The transporter-facing portal. Every action is scoped to the caller's own transporter: an invitation or load
/// belonging to another transporter is reported as not found.
/// </summary>
[ApiController]
[Route("api/v1/vendor")]
[Produces("application/json")]
[VendorUsersOnly]
public sealed class VendorController(
    ICurrentUser currentUser,
    ITenderQueries queries,
    ITenderResponseService responses,
    ITransporterQueries transporters,
    IPlacementService placements,
    IPodService pods,
    IDriverService drivers,
    IDocumentService documents,
    ICapacityService capacity) : ControllerBase
{
    private long TransporterId => currentUser.TransporterId ?? throw new InvalidOperationException("Vendor scope is missing.");

    [HttpGet("dashboard")]
    [ProducesResponseType<VendorDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<VendorDashboardDto>> Dashboard(CancellationToken cancellationToken) =>
        Ok(await queries.VendorDashboardAsync(TransporterId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken));

    [HttpGet("profile")]
    public async Task<ActionResult<TransporterDetail>> Profile(CancellationToken cancellationToken) =>
        await transporters.GetTransporterAsync(TransporterId, cancellationToken) is { } detail ? Ok(detail) : NotFound();

    [HttpGet("tenders")]
    [ProducesResponseType<PagedResult<TenderInvitationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TenderInvitationDto>>> Tenders(
        [FromQuery] TenderStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await queries.VendorInvitationsAsync(TransporterId, status, page, pageSize, cancellationToken));

    /// <summary>Opening a tender marks it as viewed.</summary>
    [HttpGet("tenders/{id:long}")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenderDetailDto>> Tender(long id, CancellationToken cancellationToken) =>
        Ok(await responses.ViewAsync(id, TransporterId, cancellationToken));

    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("tenders/{id:long}/accept")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenderDetailDto>> Accept(long id, [FromBody] AcceptTenderRequest? request, CancellationToken cancellationToken) =>
        Ok(await responses.AcceptAsync(id, TransporterId, request ?? new AcceptTenderRequest(null), cancellationToken));

    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("tenders/{id:long}/reject")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenderDetailDto>> Reject(long id, [FromBody] RejectTenderRequest request, CancellationToken cancellationToken) =>
        Ok(await responses.RejectAsync(id, TransporterId, request, cancellationToken));

    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("tenders/{id:long}/counter-offer")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenderDetailDto>> CounterOffer(long id, [FromBody] CounterOfferRequest request, CancellationToken cancellationToken) =>
        Ok(await responses.CounterOfferAsync(id, TransporterId, request, cancellationToken));

    [HttpGet("loads")]
    [ProducesResponseType<IReadOnlyList<VendorLoadDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VendorLoadDto>>> Loads(CancellationToken cancellationToken) =>
        Ok(await queries.VendorLoadsAsync(TransporterId, cancellationToken));

    /// <summary>Confirms the vehicle and driver for an accepted load. The vehicle must belong to the caller's fleet.</summary>
    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("loads/{id:long}/vehicle")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenderDetailDto>> AssignVehicle(long id, [FromBody] VehicleAssignmentRequest request, CancellationToken cancellationToken) =>
        Ok(await responses.AssignVehicleAsync(id, TransporterId, request, cancellationToken));

    [HttpGet("placements")]
    [ProducesResponseType<PagedResult<PlacementDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PlacementDto>>> Placements([FromQuery] PlacementStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await placements.ListAsync(null, status, TransporterId, page, pageSize, cancellationToken));

    /// <summary>The transporter confirms it will place a vehicle for this load.</summary>
    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("placements/{id:long}/confirm")]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlacementDto>> ConfirmPlacement(long id, CancellationToken cancellationToken) =>
        Ok(await placements.ConfirmAsync(id, TransporterId, cancellationToken));

    /// <summary>The transporter reports that the assigned vehicle is on its way to the site.</summary>
    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("placements/{id:long}/report")]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PlacementDto>> ReportPlacement(long id, CancellationToken cancellationToken) =>
        Ok(await placements.ReportAsync(id, TransporterId, cancellationToken));

    [HttpGet("pods")]
    [ProducesResponseType<PagedResult<PodDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PodDto>>> Pods([FromQuery] PodStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await pods.ListAsync(null, status, TransporterId, page, pageSize, cancellationToken));

    [HttpGet("pods/{id:long}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PodFile(long id, CancellationToken cancellationToken)
    {
        var file = await pods.OpenFileAsync(id, TransporterId, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Submits the proof of delivery for a delivered load. Allowed while the POD is pending or has been sent back.</summary>
    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("loads/{loadReference}/pod")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PodDto>> SubmitPod(string loadReference, [FromForm] PodUploadForm form, CancellationToken cancellationToken)
    {
        var metadata = new PodSubmissionRequest(
            form.File?.FileName ?? string.Empty,
            form.File?.ContentType ?? string.Empty,
            form.File?.Length ?? 0,
            form.PodDate,
            form.ReceivedBy ?? string.Empty);
        await using var content = form.File?.OpenReadStream() ?? Stream.Null;
        return Ok(await pods.SubmitAsync(loadReference, TransporterId, metadata, content, cancellationToken));
    }
    /// <summary>The transporter's own drivers. Vendors cannot change a driver's status; internal compliance does that.</summary>
    [HttpGet("drivers")]
    [ProducesResponseType<IReadOnlyList<DriverDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DriverDto>>> Drivers(CancellationToken cancellationToken) =>
        Ok(await drivers.ListAsync(TransporterId, cancellationToken));

    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("drivers")]
    [ProducesResponseType<DriverDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DriverDto>> AddDriver([FromBody] SaveDriverRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await drivers.CreateAsync(TransporterId, request with { Status = null }, cancellationToken));

    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPut("drivers/{driverId:long}")]
    [ProducesResponseType<DriverDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverDto>> UpdateDriver(long driverId, [FromBody] SaveDriverRequest request, CancellationToken cancellationToken) =>
        Ok(await drivers.UpdateAsync(TransporterId, driverId, request with { Status = null }, cancellationToken));

    /// <summary>Uploads a driver document, such as a licence. It goes to compliance for verification like any other document.</summary>
    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPost("drivers/{driverId:long}/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentRules.MaxFileSizeBytes + 1024 * 1024)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentDto>> UploadDriverDocument(long driverId, [FromForm] DocumentUploadForm form, CancellationToken cancellationToken)
    {
        var details = new SaveDocumentRequest(form.DocumentTypeId, form.DocumentNumber, form.IssueDate, form.ExpiryDate, null, form.Remarks, driverId);
        if (form.File is null)
        {
            var empty = new DocumentUpload(details, string.Empty, string.Empty, 0, Stream.Null);
            return StatusCode(StatusCodes.Status201Created, await documents.UploadAsync(TransporterId, empty, cancellationToken));
        }

        await using var content = form.File.OpenReadStream();
        var upload = new DocumentUpload(details, form.File.FileName, form.File.ContentType, form.File.Length, content);
        return StatusCode(StatusCodes.Status201Created, await documents.UploadAsync(TransporterId, upload, cancellationToken));
    }

    /// <summary>Vehicle capacity by day, for the vendor's own transporter. Feeds the availability KPI.</summary>
    [HttpGet("capacity")]
    [ProducesResponseType<IReadOnlyList<CapacityDayDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CapacityDayDto>>> Capacity([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        Ok(await capacity.ListAsync(TransporterId, from, to, cancellationToken));

    [VendorUsersOnly(AccessRoles.VendorWriters)]
    [HttpPut("capacity")]
    [ProducesResponseType<CapacityDayDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CapacityDayDto>> SaveCapacity([FromBody] SaveCapacityRequest request, CancellationToken cancellationToken) =>
        Ok(await capacity.SaveAsync(TransporterId, request, cancellationToken));
}

/// <summary>Multipart form for a POD submission.</summary>
public sealed class PodUploadForm
{
    public IFormFile? File { get; set; }

    public DateOnly PodDate { get; set; }

    public string? ReceivedBy { get; set; }
}
