using GSM.API.Auth;
using GSM.API.Uploads;
using GSM.Application.Common;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Communications;
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

    /// <summary>
    /// Cuántos miembros recibirían un anuncio por email: los de su audiencia (nula = todos) que están activos, tienen
    /// email y el correo de su categoría encendido (Novedades o Promociones).
    /// </summary>
    [HttpPost("email-audience")]
    [EndpointName("EmailAudience")]
    [ProducesResponseType<EmailAudienceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EmailAudienceResponse>> EmailAudience(
        EmailAudienceRequest request,
        [FromServices] GetEmailAudienceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>"Llegará a N miembros": calcula una audiencia (filtros vacíos = todos) sin guardar nada.</summary>
    [HttpPost("audience-preview")]
    [ProducesResponseType<AudiencePreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AudiencePreviewResponse>> AudiencePreview(
        AnnouncementAudienceDto request,
        [FromServices] PreviewAnnouncementAudienceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Etiquetas en uso entre los miembros activos, para el filtro por etiqueta.</summary>
    [HttpGet("audience-tags")]
    [ProducesResponseType<AudienceTagsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AudienceTagsResponse>> AudienceTags(
        [FromServices] GetAudienceTagsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// A quién llegó el anuncio (segmentado y publicado: los destinatarios fijados) o a quién llegaría hoy.
    /// </summary>
    [HttpGet("{id:guid}/recipients")]
    [ProducesResponseType<AudiencePreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AudiencePreviewResponse>> Recipients(
        Guid id, [FromServices] GetAnnouncementRecipientsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

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

    /// <summary>Felicitación de cumpleaños del gym: si está encendida y su texto (§F).</summary>
    [HttpGet("/api/communications/birthday-settings")]
    [ProducesResponseType<BirthdaySettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BirthdaySettingsResponse>> BirthdaySettings(
        [FromServices] GetBirthdaySettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Enciende o apaga la felicitación de cumpleaños y fija su texto. Solo Owner.</summary>
    [HttpPut("/api/communications/birthday-settings")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<BirthdaySettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BirthdaySettingsResponse>> UpdateBirthdaySettings(
        UpdateBirthdaySettingsRequest request,
        [FromServices] UpdateBirthdaySettingsHandler handler,
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

    /// <summary>
    /// CO4: programa (o reprograma) la publicación para una hora futura. La publica la tarea periódica, unos
    /// minutos después de la hora elegida como mucho, nunca antes. Publicado o archivado → 409; pasada → 422.
    /// </summary>
    [HttpPost("{id:guid}/schedule")]
    [EndpointName("ScheduleAnnouncement")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AnnouncementResponse>> Schedule(
        Guid id,
        ScheduleAnnouncementRequest request,
        [FromServices] ScheduleAnnouncementHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>CO4: cancela la programación; el anuncio vuelve a borrador.</summary>
    [HttpPost("{id:guid}/unschedule")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AnnouncementResponse>> Unschedule(
        Guid id, [FromServices] UnscheduleAnnouncementHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>CO4: sube o reemplaza la imagen (JPG, PNG o WebP, hasta 5 MB). Reemplaza y borra la anterior.</summary>
    [HttpPost("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AnnouncementResponse>> UploadImage(
        Guid id,
        IFormFile file,
        [FromServices] UploadAnnouncementImageHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, file.ToFileToStore(), cancellationToken));

    [HttpDelete("{id:guid}/image")]
    [ProducesResponseType<AnnouncementResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnnouncementResponse>> RemoveImage(
        Guid id, [FromServices] RemoveAnnouncementImageHandler handler, CancellationToken cancellationToken)
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
