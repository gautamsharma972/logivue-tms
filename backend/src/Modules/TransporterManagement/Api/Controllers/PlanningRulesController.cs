using LogiVue.Tms.TransporterManagement.Application.Planning;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Configurable planning classifications for a transporter: preferred, restricted and similar.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}/planning-rules")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class PlanningRulesController(IPlanningRuleService rules) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlanningRuleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlanningRuleDto>>> List(long transporterId, CancellationToken cancellationToken) =>
        Ok(await rules.ListAsync(transporterId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<PlanningRuleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Authorize(Roles = AccessRoles.Managers)]
    public async Task<ActionResult<PlanningRuleDto>> Add(long transporterId, [FromBody] PlanningRuleRequest request, CancellationToken cancellationToken)
    {
        var created = await rules.AddAsync(transporterId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPost("{ruleId:long}/deactivate")]
    [ProducesResponseType<PlanningRuleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Authorize(Roles = AccessRoles.Managers)]
    public async Task<ActionResult<PlanningRuleDto>> Deactivate(long transporterId, long ruleId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await rules.DeactivateAsync(transporterId, ruleId, request, cancellationToken));
}
