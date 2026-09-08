using GSM.API.Auth;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Membresías de un miembro concreto.</summary>
[ApiController]
[Route("api/members/{memberId:guid}/memberships")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MemberMembershipsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MembershipResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MembershipResponse>>> List(
        Guid memberId,
        [FromServices] ListMemberMembershipsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<MembershipResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MembershipResponse>> Assign(
        Guid memberId,
        AssignMembershipRequest request,
        [FromServices] AssignMembershipHandler handler,
        CancellationToken cancellationToken)
    {
        var membership = await handler.HandleAsync(memberId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { memberId }, membership);
    }
}
