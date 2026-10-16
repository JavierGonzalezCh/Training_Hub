using GSM.API.Auth;
using GSM.Application.Members;
using GSM.Application.Members.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Ajustes de Members del gym (M6): el plazo de anonimización.</summary>
[ApiController]
[Route("api/member-settings")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MemberSettingsController : ControllerBase
{
    [HttpGet]
    [EndpointName("GetMemberSettings")]
    [ProducesResponseType<MemberSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberSettingsResponse>> Get(
        [FromServices] GetMemberSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [EndpointName("UpdateMemberSettings")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<MemberSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MemberSettingsResponse>> Update(
        UpdateMemberSettingsRequest request,
        [FromServices] UpdateMemberSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}
