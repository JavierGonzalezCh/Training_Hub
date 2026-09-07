using GSM.API.Auth;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Alta de gyms. Solo el superadmin.</summary>
[ApiController]
[Route("api/gyms")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class GymsController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateGymResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateGymResponse>> Create(
        CreateGymRequest request, [FromServices] CreateGymHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return Created((string?)null, result);
    }
}
