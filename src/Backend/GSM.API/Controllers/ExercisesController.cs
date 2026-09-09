using GSM.API.Auth;
using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Catálogo híbrido de ejercicios: cada gym ve los del sistema (<c>gym_id = null</c>) más los suyos.
/// Escribir sobre una fila del sistema exige superadmin; sobre una del gym, staff.
/// </summary>
[ApiController]
[Route("api/exercises")]
[Authorize]
public sealed class ExercisesController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExerciseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExerciseResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] Muscle? primaryMuscle,
        [FromQuery] Equipment? equipment,
        [FromQuery] bool includeInactive,
        [FromServices] ListExercisesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(
            new ExerciseFilter(search, primaryMuscle, equipment, includeInactive), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExerciseResponse>> Get(
        Guid id, [FromServices] GetExerciseHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.RequireStaffOrSuperAdmin)]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExerciseResponse>> Create(
        CreateExerciseRequest request,
        [FromServices] CreateExerciseHandler handler,
        CancellationToken cancellationToken)
    {
        var exercise = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = exercise.Id }, exercise);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaffOrSuperAdmin)]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ExerciseResponse>> Update(
        Guid id,
        UpdateExerciseRequest request,
        [FromServices] UpdateExerciseHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaffOrSuperAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateExerciseHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
