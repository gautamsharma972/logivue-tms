using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Coverage;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Serviceable lanes for a transporter.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/lanes")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class LanesController(ICoverageService coverage, ITransporterQueries queries) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<LaneDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LaneDto>>> Search(
        long transporterId,
        [FromQuery] long? originLocationReference,
        [FromQuery] long? destinationLocationReference,
        [FromQuery] string? serviceType,
        [FromQuery] RecordStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await queries.SearchLanesAsync(transporterId,
            new LaneSearch(originLocationReference, destinationLocationReference, serviceType, status, page, pageSize), cancellationToken));

    [HttpPost]
    [ProducesResponseType<LaneDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<LaneDto>> Add(long transporterId, [FromBody] SaveLaneRequest request, CancellationToken cancellationToken)
    {
        var lane = await coverage.AddLaneAsync(transporterId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, lane);
    }

    [HttpPut("{laneId:long}")]
    [ProducesResponseType<LaneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<LaneDto>> Update(long transporterId, long laneId, [FromBody] SaveLaneRequest request, CancellationToken cancellationToken) =>
        Ok(await coverage.UpdateLaneAsync(transporterId, laneId, request, cancellationToken));
}

/// <summary>Capabilities a transporter holds. Removal end-dates the capability rather than deleting it.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/capabilities")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class CapabilitiesController(ICoverageService coverage, ITransporterService transporters) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CapabilityDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CapabilityDto>>> List(long transporterId, CancellationToken cancellationToken)
    {
        var detail = await transporters.GetAsync(transporterId, cancellationToken);
        return Ok(detail.Capabilities);
    }

    [HttpPost]
    [ProducesResponseType<CapabilityDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<CapabilityDto>> Add(long transporterId, [FromBody] AddCapabilityRequest request, CancellationToken cancellationToken)
    {
        var capability = await coverage.AddCapabilityAsync(transporterId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, capability);
    }

    [HttpDelete("{capabilityId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<IActionResult> Remove(long transporterId, long capabilityId, CancellationToken cancellationToken)
    {
        await coverage.RemoveCapabilityAsync(transporterId, capabilityId, cancellationToken);
        return NoContent();
    }
}
