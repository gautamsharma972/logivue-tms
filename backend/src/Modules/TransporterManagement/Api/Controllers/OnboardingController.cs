using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Onboarding workflow and status changes. Each action is recorded in the approval history.</summary>
[ApiController]
[Route("api/v1/transporters/{transporterId:long}")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class OnboardingController(IOnboardingService onboarding) : ControllerBase
{
    [HttpPost("submit")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TransporterDetail>> Submit(long transporterId, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await onboarding.SubmitAsync(transporterId, request ?? new CommentsRequest(null), cancellationToken));

    /// <summary>Accepts the transporter for review, or advances it past its current review stage.</summary>
    [HttpPost("approve")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TransporterDetail>> Approve(long transporterId, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await onboarding.ApproveStageAsync(transporterId, request ?? new CommentsRequest(null), cancellationToken));

    [HttpPost("reject")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransporterDetail>> Reject(long transporterId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await onboarding.RejectAsync(transporterId, request, cancellationToken));

    [HttpPost("suspend")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TransporterDetail>> Suspend(long transporterId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await onboarding.SuspendAsync(transporterId, request, cancellationToken));

    [HttpPost("activate")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TransporterDetail>> Activate(long transporterId, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await onboarding.ActivateAsync(transporterId, request ?? new CommentsRequest(null), cancellationToken));

    [HttpPost("deactivate")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TransporterDetail>> Deactivate(long transporterId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await onboarding.DeactivateAsync(transporterId, request, cancellationToken));

    [HttpPost("blacklist")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TransporterDetail>> Blacklist(long transporterId, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await onboarding.BlacklistAsync(transporterId, request, cancellationToken));

    [HttpGet("approval-history")]
    [ProducesResponseType<IReadOnlyList<ApprovalActionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovalActionDto>>> History(long transporterId, CancellationToken cancellationToken) =>
        Ok(await onboarding.GetHistoryAsync(transporterId, cancellationToken));
}
