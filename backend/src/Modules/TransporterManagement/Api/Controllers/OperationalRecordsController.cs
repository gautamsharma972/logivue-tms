using LogiVue.Tms.TransporterManagement.Api.Filters;
using LogiVue.Tms.TransporterManagement.Application.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>
/// Claims, invoiced cost and vehicle capacity recorded against a transporter. Each write rebuilds the affected month's
/// KPIs, so scorecards and rankings follow these records without anyone editing a KPI by hand.
/// </summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class OperationalRecordsController(IClaimService claims, ILoadCostService costs, ICapacityService capacity) : ControllerBase
{
    [HttpGet("claims")]
    [ProducesResponseType<IReadOnlyList<ClaimDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> ListClaims(long transporterId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        Ok(await claims.ListAsync(transporterId, from, to, cancellationToken));

    [HttpPost("claims")]
    [Authorize(Roles = AccessRoles.Operations)]
    [ProducesResponseType<ClaimDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ClaimDto>> RecordClaim(long transporterId, [FromBody] RecordClaimRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await claims.RecordAsync(transporterId, request, cancellationToken));

    [HttpPost("claims/{claimId:long}/resolve")]
    [Authorize(Roles = AccessRoles.Operations)]
    [ProducesResponseType<ClaimDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClaimDto>> ResolveClaim(long transporterId, long claimId, CancellationToken cancellationToken) =>
        Ok(await claims.ResolveAsync(transporterId, claimId, cancellationToken));

    [HttpPost("load-costs")]
    [Authorize(Roles = AccessRoles.Operations)]
    [ProducesResponseType<LoadCostDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<LoadCostDto>> RecordLoadCost(long transporterId, [FromBody] RecordLoadCostRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await costs.RecordAsync(transporterId, request, cancellationToken));

    [HttpGet("capacity")]
    [ProducesResponseType<IReadOnlyList<CapacityDayDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CapacityDayDto>>> ListCapacity(long transporterId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        Ok(await capacity.ListAsync(transporterId, from, to, cancellationToken));

    /// <summary>Corrects a day's capacity on behalf of a vendor. Vendors normally report their own through the portal.</summary>
    [HttpPut("capacity")]
    [Authorize(Roles = AccessRoles.Operations)]
    [ProducesResponseType<CapacityDayDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CapacityDayDto>> SaveCapacity(long transporterId, [FromBody] SaveCapacityRequest request, CancellationToken cancellationToken) =>
        Ok(await capacity.SaveAsync(transporterId, request, cancellationToken));
}
