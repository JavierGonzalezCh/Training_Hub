using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/members")]
[Authorize]
public sealed class MembersController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<PagedResult<MemberListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MemberListItem>>> List(
        [FromQuery] ListMembersQuery query,
        [FromServices] ListMembersHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberDetail>> Get(
        Guid id, [FromServices] GetMemberHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<CreateMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateMemberResponse>> Create(
        CreateMemberRequest request, [FromServices] CreateMemberHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<MemberDetail>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberDetail>> Update(
        Guid id,
        UpdateMemberRequest request,
        [FromServices] UpdateMemberHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateMemberHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
