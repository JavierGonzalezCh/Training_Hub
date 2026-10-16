using GSM.API.Auth;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Ajustes de rutinas del gym: cada cuántas semanas se revisa una rutina (R8).</summary>
[ApiController]
[Route("api/routine-settings")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class RoutineSettingsController : ControllerBase
{
    [HttpGet]
    [EndpointName("GetRoutineSettings")]
    [ProducesResponseType<RoutineSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoutineSettingsResponse>> Get(
        [FromServices] GetRoutineSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [EndpointName("UpdateRoutineSettings")]
    [ProducesResponseType<RoutineSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoutineSettingsResponse>> Update(
        UpdateRoutineSettingsRequest request,
        [FromServices] UpdateRoutineSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}
