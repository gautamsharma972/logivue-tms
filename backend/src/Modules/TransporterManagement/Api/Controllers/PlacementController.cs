using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>
/// Internal vehicle placement: requests, placement at site, no-shows and cancellations. Transporter steps
/// (confirm, report) are on the vendor portal.
/// </summary>
[ApiController]
[Route("api/v1/vehicle-placement")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class PlacementController(IPlacementService placements) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<PlacementDto>> Create([FromBody] CreatePlacementRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await placements.CreateAsync(request, cancellationToken));

    [HttpGet]
    [ProducesResponseType<PagedResult<PlacementDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PlacementDto>>> List(
        [FromQuery] long? transporterId,
        [FromQuery] PlacementStatus? status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken) =>
        Ok(await placements.ListAsync(transporterId, status, null, page < 1 ? 1 : page, pageSize < 1 ? 25 : pageSize, cancellationToken));

    [HttpGet("{id:long}")]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlacementDto>> Get(long id, CancellationToken cancellationToken) =>
        Ok(await placements.GetAsync(id, null, cancellationToken));

    [HttpPost("{id:long}/place")]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<PlacementDto>> Place(long id, CancellationToken cancellationToken) =>
        Ok(await placements.PlaceAsync(id, cancellationToken));

    [HttpPost("{id:long}/no-show")]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<PlacementDto>> NoShow(long id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await placements.NoShowAsync(id, request, cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<PlacementDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<PlacementDto>> Cancel(long id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await placements.CancelAsync(id, request, cancellationToken));
}
