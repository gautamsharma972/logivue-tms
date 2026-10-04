using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Fleet;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Transporter-owned vehicles.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/vehicles")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class FleetController(IFleetService fleet, ITransporterQueries queries) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<VehicleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<VehicleDto>>> Search(
        long transporterId,
        [FromQuery] VehicleAvailabilityStatus? availability,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await queries.SearchVehiclesAsync(transporterId, new VehicleSearch(availability, search, page, pageSize), cancellationToken));

    [HttpPost]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<VehicleDto>> Add(long transporterId, [FromBody] SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await fleet.AddVehicleAsync(transporterId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, vehicle);
    }

    [HttpPut("{vehicleId:long}")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<VehicleDto>> Update(long transporterId, long vehicleId, [FromBody] SaveVehicleRequest request, CancellationToken cancellationToken) =>
        Ok(await fleet.UpdateVehicleAsync(transporterId, vehicleId, request, cancellationToken));
}
