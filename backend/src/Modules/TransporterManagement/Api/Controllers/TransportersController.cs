using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Transporter master data, with contacts and branches.</summary>
[ApiController]
[Route("api/v1/transporters")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class TransportersController(ITransporterService transporters) : ControllerBase
{
    /// <summary>Searches transporters server-side with paging. Filters combine with AND.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TransporterListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TransporterListItem>>> Search(
        [FromQuery] string? search,
        [FromQuery] TransporterStatus? status,
        [FromQuery] long? transporterTypeId,
        [FromQuery] string? city,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await transporters.SearchAsync(
            new TransporterSearch(search, status, transporterTypeId, city, page, pageSize), cancellationToken));

    [HttpGet("{id:long}")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransporterDetail>> Get(long id, CancellationToken cancellationToken) =>
        Ok(await transporters.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<TransporterDetail>> Create([FromBody] CreateTransporterRequest request, CancellationToken cancellationToken)
    {
        var created = await transporters.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<TransporterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<TransporterDetail>> Update(long id, [FromBody] UpdateTransporterRequest request, CancellationToken cancellationToken) =>
        Ok(await transporters.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{transporterId:long}/contacts")]
    [ProducesResponseType<ContactDto>(StatusCodes.Status201Created)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<ContactDto>> AddContact(long transporterId, [FromBody] SaveContactRequest request, CancellationToken cancellationToken)
    {
        var contact = await transporters.AddContactAsync(transporterId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, contact);
    }

    [HttpPut("{transporterId:long}/contacts/{contactId:long}")]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<ContactDto>> UpdateContact(long transporterId, long contactId, [FromBody] SaveContactRequest request, CancellationToken cancellationToken) =>
        Ok(await transporters.UpdateContactAsync(transporterId, contactId, request, cancellationToken));

    [HttpPost("{transporterId:long}/branches")]
    [ProducesResponseType<BranchDto>(StatusCodes.Status201Created)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<BranchDto>> AddBranch(long transporterId, [FromBody] SaveBranchRequest request, CancellationToken cancellationToken)
    {
        var branch = await transporters.AddBranchAsync(transporterId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, branch);
    }

    [HttpPut("{transporterId:long}/branches/{branchId:long}")]
    [ProducesResponseType<BranchDto>(StatusCodes.Status200OK)]
    [Authorize(Roles = AccessRoles.MasterData)]
    public async Task<ActionResult<BranchDto>> UpdateBranch(long transporterId, long branchId, [FromBody] SaveBranchRequest request, CancellationToken cancellationToken) =>
        Ok(await transporters.UpdateBranchAsync(transporterId, branchId, request, cancellationToken));
}
