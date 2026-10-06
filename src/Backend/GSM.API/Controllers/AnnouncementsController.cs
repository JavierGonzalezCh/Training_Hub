using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Anuncios del gym (etapa 13.1), gestionados por Owner y Staff. El miembro los lee en
/// <c>GET /api/me/feed</c>. Con <c>show_community = false</c> el staff sigue pudiendo prepararlos.
/// </summary>
[ApiController]
[Route("api/announcements")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class AnnouncementsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AnnouncementResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AnnouncementResponse>>> List(
        [FromQuery] ListAnnouncementsQuery query,
        [FromServices] ListAnnouncementsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    /// <summary>Cuántos miembros recibirían un anuncio por email (activos, con email y sin baja).</summary>
    [HttpGet("email-audience")]
    [ProducesResponseType<EmailAudienceResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailAudienceResponse>> EmailAudience(
        [FromServices] GetEmailAudienceHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpGet("/api/communications/reminder-settings")]
    [ProducesResponseType<CommunicationsSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CommunicationsSettingsResponse>> Settings(
        [FromServices] GetCommunicationsSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Días de antelación del recordatorio de vencimiento (0 = desactivado). Solo Owner.</summary>
    [HttpPut("/api/communications/reminder-settings")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<CommunicationsSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CommunicationsSettingsResponse>> UpdateSettings(
        UpdateCommunicationsSettingsRequest request,
        [FromServices] UpdateCommunicationsSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnnouncementResponse>> Get(
        Guid id, [FromServices] GetAnnouncementHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Crea un borrador. Fijarlo al crearlo cuenta para el límite de 3 fijados (409).</summary>
    [HttpPost]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnnouncementResponse>> Create(
        CreateAnnouncementRequest request,
        [FromServices] CreateAnnouncementHandler handler,
        CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Edita el contenido. Sobre un publicado no reenvía correos. Archivado → 409.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnnouncementResponse>> Update(
        Guid id,
        UpdateAnnouncementRequest request,
        [FromServices] UpdateAnnouncementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Publica. Ya publicado o archivado → 409; caducidad pasada → 422.</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AnnouncementResponse>> Publish(
        Guid id, [FromServices] PublishAnnouncementHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnnouncementResponse>> Archive(
        Guid id, [FromServices] ArchiveAnnouncementHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Fija el anuncio. Con 3 ya fijados → 409.</summary>
    [HttpPost("{id:guid}/pin")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnnouncementResponse>> Pin(
        Guid id, [FromServices] PinAnnouncementHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, pinned: true, cancellationToken));

    [HttpPost("{id:guid}/unpin")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnnouncementResponse>> Unpin(
        Guid id, [FromServices] PinAnnouncementHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, pinned: false, cancellationToken));
}
