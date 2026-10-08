using GSM.Domain.Equipment;
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
        [FromQuery] EquipmentType? equipment,
        [FromQuery] bool includeInactive,
        [FromServices] ListExercisesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] MuscleGroup? muscleGroup = null,
        [FromQuery] ExerciseType? exerciseType = null,
        [FromQuery] Difficulty? difficulty = null,
        [FromQuery] bool includeHidden = false)
        => Ok(await handler.HandleAsync(
            new ExerciseFilter(
                search, primaryMuscle, equipment, includeInactive,
                muscleGroup, exerciseType, difficulty, includeHidden),
            cancellationToken));

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

    /// <summary>Copia del ejercicio para el gym del staff (todos los campos y los mismos archivos de medios).</summary>
    [HttpPost("{id:guid}/duplicate")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<ExerciseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExerciseResponse>> Duplicate(
        Guid id,
        [FromServices] DuplicateExerciseHandler handler,
        CancellationToken cancellationToken,
        [FromBody] DuplicateExerciseRequest? request = null)
    {
        var exercise = await handler.HandleAsync(id, request ?? new DuplicateExerciseRequest(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = exercise.Id }, exercise);
    }

    /// <summary>Oculta un ejercicio del sistema para el gym. Uno del gym → 422.</summary>
    [HttpPost("{id:guid}/hide")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Hide(
        Guid id, [FromServices] HideExerciseHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Vuelve a mostrar un ejercicio del sistema que el gym había ocultado.</summary>
    [HttpPost("{id:guid}/show")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Show(
        Guid id, [FromServices] ShowExerciseHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
