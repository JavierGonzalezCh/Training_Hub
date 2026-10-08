using GSM.API.Auth;
using GSM.Application.Achievements;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Application.Common;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
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

    [HttpGet("communication-preferences")]
    [ProducesResponseType<CommunicationPreferencesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CommunicationPreferencesResponse>> CommunicationPreferences(
        [FromServices] GetCommunicationPreferencesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Activa o desactiva los avisos del gym por email (no afecta a los transaccionales).</summary>
    [HttpPut("communication-preferences")]
    [ProducesResponseType<CommunicationPreferencesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CommunicationPreferencesResponse>> UpdateCommunicationPreferences(
        UpdateCommunicationPreferencesRequest request,
        [FromServices] UpdateCommunicationPreferencesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpGet("feed/unread-count")]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount(
        [FromServices] GetUnreadCountHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
