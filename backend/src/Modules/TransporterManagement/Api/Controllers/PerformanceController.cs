using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Internal performance: operational metrics for a period, and KPI recalculation for correcting history.</summary>
[ApiController]
[Route("api/v1/transporters/performance")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class PerformanceController(IPerformanceService performance) : ControllerBase
{
    /// <summary>Calculates the KPIs and operational metrics for a period without storing them.</summary>
    [HttpGet("{transporterId:long}/operations")]
    [ProducesResponseType<OperationalPeriodResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperationalPeriodResult>> Operations(
        long transporterId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken) =>
        Ok(await performance.GetOperationsAsync(transporterId, from, to, cancellationToken));

    /// <summary>Rebuilds the stored monthly KPIs for the range from the operational records. Safe to repeat.</summary>
    [HttpPost("recalculate")]
    [ProducesResponseType<IReadOnlyList<OperationalPeriodResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Performance)]
    public async Task<ActionResult<IReadOnlyList<OperationalPeriodResult>>> Recalculate([FromBody] RecalculationRequest request, CancellationToken cancellationToken) =>
        Ok(await performance.RecalculateAsync(request.TransporterId, request.From, request.To, cancellationToken));
}
