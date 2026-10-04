using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Alerts;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

[ApiController]
[Route("api/v1/transporter-management/alerts")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class AlertsController(IAlertService alerts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AlertDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AlertDto>>> Search(
        [FromQuery] long? transporterId,
        [FromQuery] AlertStatus? status,
        [FromQuery] Severity? severity,
        [FromQuery] string? alertType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await alerts.SearchAsync(new AlertSearch(transporterId, status, severity, alertType, page, pageSize), cancellationToken));

    [HttpPost("{alertId:long}/acknowledge")]
    [ProducesResponseType<AlertDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<AlertDto>> Acknowledge(long alertId, CancellationToken cancellationToken) =>
        Ok(await alerts.AcknowledgeAsync(alertId, cancellationToken));

    [HttpPost("{alertId:long}/resolve")]
    [ProducesResponseType<AlertDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Operations)]
    public async Task<ActionResult<AlertDto>> Resolve(long alertId, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await alerts.ResolveAsync(alertId, request?.Comments, cancellationToken));
}
