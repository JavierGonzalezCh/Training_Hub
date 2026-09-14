using GSM.API.Auth;
using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.MemberStreak;
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

    /// <summary>Auto-servicio: el socio se arma su propia rutina. 403 si <c>members_can_create_routines = false</c>.</summary>
    [HttpPost("routines")]
    [ProducesResponseType<RoutineResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RoutineResponse>> CreateRoutine(
        SaveRoutineRequest request,
        [FromServices] SelfAssignRoutineHandler handler,
        CancellationToken cancellationToken)
    {
        var routine = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Routines), routine);
    }

    [HttpGet("routines/history")]
    [ProducesResponseType<IReadOnlyList<RoutineHistoryItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RoutineHistoryItem>>> RoutineHistory(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] GetMyRoutineHistoryHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));

    /// <summary>Horario de clases del gym para la app del socio. 403 si <c>show_classes = false</c>.</summary>
    [HttpGet("classes")]
    [ProducesResponseType<ScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ScheduleResponse>> Classes(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] GetMyClassesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));

    /// <summary>Histórico de clases del socio: todas sus reservas y asistencias.</summary>
    [HttpGet("classes/history")]
    [ProducesResponseType<IReadOnlyList<ClassHistoryItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ClassHistoryItem>>> ClassHistory(
        [FromServices] GetMyClassHistoryHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Racha del socio + congeladores disponibles.</summary>
    [HttpGet("streak")]
    [ProducesResponseType<MemberStreakResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberStreakResponse>> Streak(
        [FromServices] GetMyStreakHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
