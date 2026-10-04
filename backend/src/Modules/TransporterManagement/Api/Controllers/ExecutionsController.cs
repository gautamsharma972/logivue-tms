using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using LogiVue.Tms.TransporterManagement.Application.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Internal load execution: pickup and delivery events, with delay attribution.</summary>
[ApiController]
[Route("api/v1/executions")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class ExecutionsController(IExecutionService executions) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<LoadExecutionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<LoadExecutionDto>> Create([FromBody] CreateExecutionRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await executions.CreateAsync(request, cancellationToken));

    [HttpGet("{id:long}")]
    [ProducesResponseType<LoadExecutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LoadExecutionDto>> Get(long id, CancellationToken cancellationToken) =>
        Ok(await executions.GetAsync(id, cancellationToken));

    /// <summary>Records one operational event. Events must follow the operational sequence and be recorded once each.</summary>
    [HttpPost("{id:long}/events")]
    [ProducesResponseType<LoadExecutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<LoadExecutionDto>> RecordEvent(long id, [FromBody] RecordExecutionEventRequest request, CancellationToken cancellationToken) =>
        Ok(await executions.RecordEventAsync(id, request, cancellationToken));
}
