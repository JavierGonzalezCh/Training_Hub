using GSM.API.Auth;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Gracia y ventana de "por vencer" del gym (antes la gracia vivía en los ajustes de check-in).</summary>
[ApiController]
[Route("api/membership-settings")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MembershipSettingsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MembershipSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MembershipSettingsResponse>> Get(
        [FromServices] GetMembershipSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<MembershipSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MembershipSettingsResponse>> Update(
        UpdateMembershipSettingsRequest request,
        [FromServices] UpdateMembershipSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}
