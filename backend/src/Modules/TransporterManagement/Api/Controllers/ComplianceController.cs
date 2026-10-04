using LogiVue.Tms.TransporterManagement.Application.Compliance;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class ComplianceController(IComplianceService compliance) : ControllerBase
{
    /// <summary>Compliance report for a transporter: approval and allocation blocks, and every document check.</summary>
    [HttpGet("transporters/{transporterId:long}/compliance")]
    [ProducesResponseType<ComplianceReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ComplianceReportDto>> Report(long transporterId, CancellationToken cancellationToken) =>
        Ok(await compliance.GetReportAsync(transporterId, cancellationToken));

    /// <summary>Runs compliance evaluation now, for one transporter or all of them, raising and resolving alerts.</summary>
    [HttpPost("transporter-management/compliance/evaluate")]
    [ProducesResponseType<ComplianceEvaluationResult>(StatusCodes.Status200OK)]
    [Authorize(Roles = AccessRoles.Compliance)]
    public async Task<ActionResult<ComplianceEvaluationResult>> Evaluate([FromQuery] long? transporterId, CancellationToken cancellationToken) =>
        Ok(await compliance.EvaluateAsync(transporterId, cancellationToken));
}
