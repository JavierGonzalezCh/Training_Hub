using GSM.API.Auth;
using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Endpoints de la app del miembro sobre su propia cuenta.</summary>
[ApiController]
[Route("api/me")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MeController : ControllerBase
{
    [HttpGet("achievements")]
    [ProducesResponseType<IReadOnlyList<MemberAchievementItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MemberAchievementItem>>> Achievements(
        [FromServices] GetMyAchievementsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Rutina activa del socio + qué toca hoy (o descanso). 403 si <c>show_routines = false</c>.</summary>
    [HttpGet("routines")]
    [ProducesResponseType<MyRoutineResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MyRoutineResponse>> Routines(
        [FromServices] GetMyRoutineHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
