using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    /// <summary>Genera un código para que el miembro cree su cuenta de la app.</summary>
    [HttpPost("{id:guid}/invite")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<InviteMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InviteMemberResponse>> Invite(
        Guid id, [FromServices] InviteMemberHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Canje anónimo del código: crea la cuenta del miembro y lo vincula.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<RegisterMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterMemberResponse>> Register(
        RegisterMemberRequest request,
        [FromServices] RegisterMemberHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.MemberId }, result);
    }
}
