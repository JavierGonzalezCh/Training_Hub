using GSM.API.Auth;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Seguimiento del miembro sobre las líneas de su rutina. Idempotente por día.</summary>
[ApiController]
[Route("api/routine-exercises")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class RoutineExercisesController : ControllerBase
{
    [HttpPost("{id:guid}/log")]
    [ProducesResponseType<RoutineLogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoutineLogResponse>> Log(
        Guid id,
        LogRoutineExerciseRequest request,
        [FromServices] LogRoutineExerciseHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request ?? new LogRoutineExerciseRequest(null, null), cancellationToken));

    [HttpDelete("{id:guid}/log")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlog(
        Guid id,
        [FromQuery] DateOnly? date,
        [FromServices] UnlogRoutineExerciseHandler handler,
        [FromServices] GSM.Application.Common.Abstractions.IClock clock,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, date ?? clock.Today, cancellationToken);
        return NoContent();
    }
}
