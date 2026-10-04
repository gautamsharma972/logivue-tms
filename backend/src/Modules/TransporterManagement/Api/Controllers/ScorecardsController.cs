using LogiVue.Tms.TransporterManagement.Api.Filters;
using LogiVue.Tms.TransporterManagement.Application.Scorecards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Transporter scorecards: a weighted score over a period, built from stored KPIs.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/scorecards")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class ScorecardsController(IScorecardService scorecards) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ScorecardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ScorecardDto>>> List(long transporterId, CancellationToken cancellationToken) =>
        Ok(await scorecards.ListAsync(transporterId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = AccessRoles.Performance)]
    [ProducesResponseType<ScorecardDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ScorecardDto>> Generate(long transporterId, [FromBody] GenerateScorecardRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await scorecards.GenerateAsync(transporterId, request, cancellationToken));
}
