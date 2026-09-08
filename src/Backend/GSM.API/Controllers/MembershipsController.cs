using GSM.API.Auth;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/memberships")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MembershipsController : ControllerBase
{
    [HttpGet("expiring")]
    [ProducesResponseType<IReadOnlyList<ExpiringMembershipItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExpiringMembershipItem>>> Expiring(
        [FromQuery] int days,
        [FromServices] GetExpiringMembershipsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(days <= 0 ? 7 : days, cancellationToken));
}
