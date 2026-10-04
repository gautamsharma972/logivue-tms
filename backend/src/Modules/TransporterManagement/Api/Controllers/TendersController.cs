using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Internal tender management: create, distribute, award and monitor tenders.</summary>
[ApiController]
[Route("api/v1/tenders")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class TendersController(ITenderService tenders, ITenderResponseService responses, ITenderQueries queries) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<IReadOnlyList<TenderInvitationDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Tendering)]
    public async Task<ActionResult<IReadOnlyList<TenderInvitationDto>>> Create([FromBody] CreateTenderRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await tenders.CreateAsync(request, cancellationToken));

    [HttpGet]
    [ProducesResponseType<PagedResult<TenderInvitationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TenderInvitationDto>>> Search(
        [FromQuery] string? tenderNumber,
        [FromQuery] TenderStatus? status,
        [FromQuery] long? transporterId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await queries.SearchAsync(new TenderSearch(tenderNumber, status, transporterId, page, pageSize), cancellationToken));

    /// <summary>Returns the invitation together with its tender group, responses and event history.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenderDetailDto>> Get(long id, CancellationToken cancellationToken) =>
        await queries.GetDetailAsync(id, null, cancellationToken) is { } detail
            ? Ok(detail)
            : NotFound(new { code = "TENDER_NOT_FOUND" });

    [HttpPost("{id:long}/send")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Tendering)]
    public async Task<ActionResult<TenderDetailDto>> Send(long id, CancellationToken cancellationToken) =>
        Ok(await tenders.SendAsync(id, cancellationToken));

    [HttpPost("{id:long}/award")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Tendering)]
    public async Task<ActionResult<TenderDetailDto>> Award(long id, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await tenders.AwardAsync(id, request ?? new CommentsRequest(null), cancellationToken));

    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Tendering)]
    public async Task<ActionResult<TenderDetailDto>> Cancel(long id, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await tenders.CancelAsync(id, request ?? new CommentsRequest(null), cancellationToken));

    /// <summary>Records an acceptance received by phone or email. The same rules apply as in the vendor portal.</summary>
    [HttpPost("{id:long}/counter-offer/accept")]
    [Authorize(Roles = AccessRoles.Tendering)]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenderDetailDto>> AcceptCounterOffer(long id, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await responses.AcceptCounterOfferAsync(id, request ?? new CommentsRequest(null), cancellationToken));

    [HttpPost("{id:long}/counter-offer/decline")]
    [Authorize(Roles = AccessRoles.Tendering)]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenderDetailDto>> DeclineCounterOffer(long id, [FromBody] CommentsRequest? request, CancellationToken cancellationToken) =>
        Ok(await responses.DeclineCounterOfferAsync(id, request ?? new CommentsRequest(null), cancellationToken));

    [HttpPost("{id:long}/accept")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Tendering)]
    public async Task<ActionResult<TenderDetailDto>> Accept(long id, [FromBody] AcceptTenderRequest? request, CancellationToken cancellationToken) =>
        Ok(await responses.AcceptAsync(id, null, request ?? new AcceptTenderRequest(null), cancellationToken));

    [HttpPost("{id:long}/reject")]
    [ProducesResponseType<TenderDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.Tendering)]
    public async Task<ActionResult<TenderDetailDto>> Reject(long id, [FromBody] RejectTenderRequest request, CancellationToken cancellationToken) =>
        Ok(await responses.RejectAsync(id, null, request, cancellationToken));
}
