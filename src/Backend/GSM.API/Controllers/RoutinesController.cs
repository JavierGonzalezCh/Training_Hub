using GSM.API.Auth;
using GSM.Application.Routines;
using GSM.Application.Routines.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Gestión de rutinas por el staff. Los endpoints del miembro viven en <c>MeController</c>.</summary>
[ApiController]
[Route("api/routines")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class RoutinesController : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<RoutineResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoutineResponse>> Get(
        Guid id, [FromServices] GetRoutineHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType<RoutineResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoutineResponse>> Update(
        Guid id,
        SaveRoutineRequest request,
        [FromServices] UpdateRoutineHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateRoutineHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>Rutinas de un miembro concreto (listar y asignar).</summary>
[ApiController]
[Route("api/members/{memberId:guid}/routines")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class MemberRoutinesController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoutineListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoutineListItem>>> List(
        Guid memberId,
        [FromServices] ListMemberRoutinesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    /// <summary>Progreso (R7): los ejercicios que el miembro ya registró, con sus récords personales. Ficha del miembro, pestaña Rutina.</summary>
    [HttpGet("progress")]
    [EndpointName("GetMemberProgress")]
    [ProducesResponseType<IReadOnlyList<ExerciseProgressSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ExerciseProgressSummary>>> Progress(
        Guid memberId, [FromServices] GetMemberProgressHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    /// <summary>Evolución de un ejercicio del miembro: un punto por día con carga y sus récords.</summary>
    [HttpGet("progress/{exerciseId:guid}")]
    [EndpointName("GetMemberExerciseProgress")]
    [ProducesResponseType<ExerciseProgressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExerciseProgressResponse>> ExerciseProgress(
        Guid memberId, Guid exerciseId,
        [FromServices] GetMemberExerciseProgressHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, exerciseId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<RoutineResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoutineResponse>> Assign(
        Guid memberId,
        SaveRoutineRequest request,
        [FromServices] AssignRoutineHandler handler,
        CancellationToken cancellationToken)
    {
        var routine = await handler.HandleAsync(memberId, request, cancellationToken);
        return CreatedAtAction(nameof(RoutinesController.Get), "Routines", new { id = routine.Id }, routine);
    }
}
