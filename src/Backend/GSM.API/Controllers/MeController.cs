using GSM.API.Auth;
using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Common;
using GSM.Application.Dashboard;
using GSM.Application.Gyms;
using GSM.Application.Gyms.Contracts;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using GSM.Application.MemberStreak;
using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
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
    /// <summary>
    /// Inicio del miembro (D2): los ocho bloques de <c>/app</c> en una llamada. Un bloque que el gym apagó, o cuyo lector
    /// falló, viaja <c>null</c>; el resto llega. Cliente: <c>GetMyHomeAsync</c>.
    /// </summary>
    [HttpGet("home")]
    [EndpointName("GetMyHome")]
    [ProducesResponseType<MemberHomeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberHomeResponse>> Home(
        [FromServices] GetMyHomeHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Datos del gimnasio del miembro: ubicación, contacto y redes ("Mi gimnasio").</summary>
    [HttpGet("gym")]
    [ProducesResponseType<MemberGymResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberGymResponse>> MyGym(
        [FromServices] GetMyGymHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Horario de atención del gimnasio: abierto o cerrado ahora, la semana y las próximas excepciones.</summary>
    [HttpGet("gym/hours")]
    [ProducesResponseType<MemberGymHoursResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberGymHoursResponse>> MyGymHours(
        [FromServices] GetMyGymHoursHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>"¿Qué tan lleno está?": tranquilo, moderado o lleno, sin número exacto. Nulo si el gym no tiene aforo.</summary>
    [HttpGet("occupancy")]
    [EndpointName("GetMyOccupancy")]
    [ProducesResponseType<MemberOccupancyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberOccupancyResponse>> MyOccupancy(
        [FromServices] GetMyOccupancyHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpGet("achievements")]
    [ProducesResponseType<IReadOnlyList<MemberAchievementItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MemberAchievementItem>>> Achievements(
        [FromServices] GetMyAchievementsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Ranking mensual de constancia: top 10, mi posición y el podio del mes anterior. 403 si el gym lo apagó.</summary>
    [HttpGet("ranking")]
    [EndpointName("GetMyRanking")]
    [ProducesResponseType<RankingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RankingResponse>> Ranking(
        [FromServices] GetMyRankingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Acepto aparecer en el ranking (o cambio cómo se me ve). Idempotente. 422 menores y alias inválido; 409 alias repetido.</summary>
    [HttpPut("ranking/participation")]
    [EndpointName("JoinMyRanking")]
    [ProducesResponseType<RankingMeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RankingMeResponse>> JoinRanking(
        JoinRankingRequest request, [FromServices] JoinRankingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Dejo de aparecer en el ranking.</summary>
    [HttpDelete("ranking/participation")]
    [EndpointName("LeaveMyRanking")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LeaveRanking(
        [FromServices] LeaveRankingHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Rutina activa del miembro + qué toca hoy (o descanso). 403 si <c>show_routines = false</c>.</summary>
    [HttpGet("routines")]
    [ProducesResponseType<MyRoutineResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MyRoutineResponse>> Routines(
        [FromServices] GetMyRoutineHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Auto-servicio: el miembro se arma su propia rutina. 403 si <c>members_can_create_routines = false</c>.</summary>
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

    /// <summary>Cumplimiento de la semana en curso: días que tocaban, días hechos y el detalle día por día. 403 si <c>show_routines = false</c>.</summary>
    [HttpGet("adherence")]
    [EndpointName("GetMyAdherence")]
    [ProducesResponseType<MemberAdherence>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MemberAdherence>> Adherence(
        [FromServices] GSM.Application.Routines.Adherence.GetMyAdherenceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Progreso (R7): los ejercicios que el miembro ya registró, con sus récords personales (mejor peso, mejor volumen…
    /// según lo que mide cada ejercicio). 403 si <c>show_routines = false</c>.
    /// </summary>
    [HttpGet("progress")]
    [EndpointName("GetMyProgress")]
    [ProducesResponseType<IReadOnlyList<ExerciseProgressSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ExerciseProgressSummary>>> Progress(
        [FromServices] GetMyProgressHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Evolución de un ejercicio del miembro: un punto por día con carga y sus récords.</summary>
    [HttpGet("progress/{exerciseId:guid}")]
    [EndpointName("GetMyExerciseProgress")]
    [ProducesResponseType<ExerciseProgressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExerciseProgressResponse>> ExerciseProgress(
        Guid exerciseId, [FromServices] GetMyExerciseProgressHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(exerciseId, cancellationToken));

    /// <summary>Horario de clases del gym para la app del miembro. 403 si <c>show_classes = false</c>.</summary>
    [HttpGet("classes")]
    [ProducesResponseType<ScheduleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ScheduleResponse>> Classes(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] GetMyClassesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));

    /// <summary>Histórico de clases del miembro: todas sus reservas y asistencias.</summary>
    [HttpGet("classes/history")]
    [ProducesResponseType<IReadOnlyList<ClassHistoryItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ClassHistoryItem>>> ClassHistory(
        [FromServices] GetMyClassHistoryHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Racha del miembro + congeladores disponibles.</summary>
    [HttpGet("streak")]
    [ProducesResponseType<MemberStreakResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberStreakResponse>> Streak(
        [FromServices] GetMyStreakHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// "Mi progreso" → Racha (L7): calendario de racha de un mes (por defecto el actual) con días entrenados, de descanso,
    /// salvados por un congelador y fallados; racha actual, mejor racha, qué toca hoy y congeladores. 422 si el mes no es válido.
    /// </summary>
    [HttpGet("streak/calendar")]
    [EndpointName("GetMyStreakCalendar")]
    [ProducesResponseType<StreakCalendarResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StreakCalendarResponse>> StreakCalendar(
        [FromQuery] int? year, [FromQuery] int? month,
        [FromServices] GetMyStreakCalendarHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(year, month, cancellationToken));

    /// <summary>"Mi membresía" (MS8): mi estado de hoy y el historial de mis membresías, con congelamientos y entradas de tiquetera.</summary>
    [HttpGet("memberships")]
    [EndpointName("GetMyMemberships")]
    [ProducesResponseType<MyMembershipsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MyMembershipsResponse>> MyMemberships(
        [FromServices] GetMyMembershipsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Anuncios publicados y vigentes del gym, fijados primero. 403 si <c>show_community = false</c>.</summary>
    [HttpGet("feed")]
    [ProducesResponseType<PagedResult<FeedItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<FeedItem>>> Feed(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] GetMemberFeedHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, cancellationToken));

    /// <summary>
    /// Reacciona a un anuncio. Con otra reacción la cambia; con la misma la quita (alterna).
    /// 404 si el anuncio no está publicado y vigente.
    /// </summary>
    [HttpPut("feed/{announcementId:guid}/reaction")]
    [ProducesResponseType<ReactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReactionResponse>> React(
        Guid announcementId,
        ReactRequest request,
        [FromServices] ReactToAnnouncementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(announcementId, request.Kind, cancellationToken));

    [HttpDelete("feed/{announcementId:guid}/reaction")]
    [ProducesResponseType<ReactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReactionResponse>> RemoveReaction(
        Guid announcementId,
        [FromServices] ReactToAnnouncementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(announcementId, null, cancellationToken));

    /// <summary>Marca los anuncios como vistos: los publicados después vuelven a contar como no leídos.</summary>
    [HttpPost("feed/seen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkFeedSeen(
        [FromServices] MarkAnnouncementsSeenHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Avisos personales del miembro: los no leídos primero.</summary>
    [HttpGet("notifications")]
    [ProducesResponseType<PagedResult<NotificationItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<NotificationItem>>> Notifications(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] ListMyNotificationsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, cancellationToken));

    [HttpPost("notifications/{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkNotificationRead(
        Guid id, [FromServices] MarkNotificationReadHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllNotificationsRead(
        [FromServices] MarkAllNotificationsReadHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Preferencias de avisos por categoría y canal (push y correo). Sin fila, los valores por defecto:
    /// todo encendido salvo Promociones. La bandeja de la app siempre recibe todo.
    /// </summary>
    [HttpGet("notification-preferences")]
    [ProducesResponseType<NotificationPreferencesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesResponse>> NotificationPreferences(
        [FromServices] GetMyNotificationPreferencesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Cambia las preferencias de las categorías enviadas; las demás conservan su valor.</summary>
    [HttpPut("notification-preferences")]
    [ProducesResponseType<NotificationPreferencesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesResponse>> UpdateNotificationPreferences(
        UpdateNotificationPreferencesRequest request,
        [FromServices] UpdateMyNotificationPreferencesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpGet("feed/unread-count")]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount(
        [FromServices] GetUnreadCountHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
