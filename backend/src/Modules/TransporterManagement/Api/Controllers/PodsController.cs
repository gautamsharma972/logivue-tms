using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using LogiVue.Tms.TransporterManagement.Application.Pod;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Internal POD review: start review, accept, reject and request resubmission.</summary>
[ApiController]
[Route("api/v1/pods")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class PodsController(IPodService pods) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<PodDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PodDto>>> List(
        [FromQuery] long? transporterId,
        [FromQuery] PodStatus? status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken) =>
        Ok(await pods.ListAsync(transporterId, status, null, page < 1 ? 1 : page, pageSize < 1 ? 25 : pageSize, cancellationToken));

    [HttpGet("{id:long}")]
    [ProducesResponseType<PodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PodDto>> Get(long id, CancellationToken cancellationToken) =>
        Ok(await pods.GetAsync(id, null, cancellationToken));

    [HttpGet("{id:long}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken)
    {
        var file = await pods.OpenFileAsync(id, null, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("{id:long}/start-review")]
    [ProducesResponseType<PodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.PodReview)]
    public async Task<ActionResult<PodDto>> StartReview(long id, CancellationToken cancellationToken) =>
        Ok(await pods.StartReviewAsync(id, cancellationToken));

    [HttpPost("{id:long}/accept")]
    [ProducesResponseType<PodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.PodReview)]
    public async Task<ActionResult<PodDto>> Accept(long id, CancellationToken cancellationToken) =>
        Ok(await pods.AcceptAsync(id, cancellationToken));

    [HttpPost("{id:long}/reject")]
    [ProducesResponseType<PodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.PodReview)]
    public async Task<ActionResult<PodDto>> Reject(long id, [FromBody] ReasonRequest request, CancellationToken cancellationToken) =>
        Ok(await pods.RejectAsync(id, request, cancellationToken));

    [HttpPost("{id:long}/request-resubmission")]
    [ProducesResponseType<PodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.PodReview)]
    public async Task<ActionResult<PodDto>> RequestResubmission(long id, CancellationToken cancellationToken) =>
        Ok(await pods.RequestResubmissionAsync(id, cancellationToken));
}
