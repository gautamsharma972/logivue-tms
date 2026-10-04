using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Configurable reference lists for dropdowns. Only active values are returned.</summary>
[ApiController]
[Route("api/v1/transporter-management/lookups")]
[Produces("application/json")]
public sealed class LookupsController(ITransporterQueries queries, IOnboardingService onboarding) : ControllerBase
{
    [HttpGet("transporter-types")]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> TransporterTypes(CancellationToken cancellationToken) =>
        Ok(await queries.GetTransporterTypesAsync(cancellationToken));

    [HttpGet("document-types")]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> DocumentTypes(CancellationToken cancellationToken) =>
        Ok(await queries.GetDocumentTypesAsync(cancellationToken));

    [HttpGet("onboarding-steps")]
    public async Task<ActionResult<IReadOnlyList<OnboardingStepDto>>> OnboardingSteps(CancellationToken cancellationToken) =>
        Ok(await onboarding.GetStepsAsync(cancellationToken));

    [HttpGet("capability-types")]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> CapabilityTypes(CancellationToken cancellationToken) =>
        Ok(await queries.GetCapabilityTypesAsync(cancellationToken));

    [HttpGet("service-types")]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> ServiceTypes(CancellationToken cancellationToken) =>
        Ok(await queries.GetServiceTypesAsync(cancellationToken));
}
