using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Module status and version. Used by the host and by smoke tests to confirm the module is wired.</summary>
[AllowAnonymous]
[ApiController]
[Route("api/v1/transporter-management/info")]
[Produces("application/json")]
public sealed class TransporterManagementInfoController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ModuleInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<ModuleInfoResponse> Get() =>
        Ok(new ModuleInfoResponse(
            Module: "TransporterManagement",
            Version: "0.1.0",
            Milestone: "1 - Foundation",
            Tables: "tm_*"));
}

public sealed record ModuleInfoResponse(string Module, string Version, string Milestone, string Tables);
