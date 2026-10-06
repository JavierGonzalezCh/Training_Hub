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

    /// <summary>Cancela una membresía asignada por error. Los pagos enlazados se anulan aparte.</summary>
    [HttpPost("{membershipId:guid}/cancel")]
    [ProducesResponseType<MembershipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MembershipResponse>> Cancel(
        Guid memberId,
        Guid membershipId,
        CancelMembershipRequest request,
        [FromServices] CancelMembershipHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, membershipId, request, cancellationToken));

    /// <summary>Congela la membresía vigente hoy.</summary>
    [HttpPost("{membershipId:guid}/pause")]
    [ProducesResponseType<MembershipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MembershipResponse>> Pause(
        Guid memberId,
        Guid membershipId,
        [FromServices] PauseMembershipHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, membershipId, cancellationToken));

    /// <summary>Reanuda una membresía congelada y corre su fin los días congelados.</summary>
    [HttpPost("{membershipId:guid}/resume")]
    [ProducesResponseType<MembershipResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MembershipResponse>> Resume(
        Guid memberId,
        Guid membershipId,
        [FromServices] ResumeMembershipHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, membershipId, cancellationToken));
}
