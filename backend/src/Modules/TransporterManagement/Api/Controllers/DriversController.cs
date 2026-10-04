using LogiVue.Tms.TransporterManagement.Api.Filters;
using LogiVue.Tms.TransporterManagement.Application.Fleet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Drivers of a transporter. Driver licences attach to these records as driver-level documents.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/drivers")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class DriversController(IDriverService drivers) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DriverDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DriverDto>>> List(long transporterId, CancellationToken cancellationToken) =>
        Ok(await drivers.ListAsync(transporterId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = AccessRoles.MasterData)]
    [ProducesResponseType<DriverDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DriverDto>> Add(long transporterId, [FromBody] SaveDriverRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await drivers.CreateAsync(transporterId, request, cancellationToken));

    [HttpPut("{driverId:long}")]
    [Authorize(Roles = AccessRoles.MasterData)]
    [ProducesResponseType<DriverDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverDto>> Update(long transporterId, long driverId, [FromBody] SaveDriverRequest request, CancellationToken cancellationToken) =>
        Ok(await drivers.UpdateAsync(transporterId, driverId, request, cancellationToken));
}
