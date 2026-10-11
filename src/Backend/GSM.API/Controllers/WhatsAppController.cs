using GSM.API.Auth;
using GSM.Application.Communications;
using GSM.Application.Communications.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// WhatsApp por enlace (Comunicaciones v2, CO3): el sistema arma el mensaje y el staff lo envía desde su propio
/// WhatsApp. Las plantillas las edita solo el Owner.
/// </summary>
[ApiController]
[Route("api/communications/whatsapp")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class WhatsAppController : ControllerBase
{
    /// <summary>Las plantillas del gym (con el texto por defecto donde no se han editado) y sus variables.</summary>
    [HttpGet("templates")]
    [ProducesResponseType<WhatsAppTemplatesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WhatsAppTemplatesResponse>> Templates(
        [FromServices] ListWhatsAppTemplatesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Edita una plantilla (<c>ExpiringSoon</c>, <c>Expired</c>, <c>AppInvitation</c>, <c>Birthday</c>). Solo Owner.</summary>
    [HttpPut("templates/{kind}")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<WhatsAppTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WhatsAppTemplateResponse>> UpdateTemplate(
        string kind,
        UpdateWhatsAppTemplateRequest request,
        [FromServices] UpdateWhatsAppTemplateHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(kind, request, cancellationToken));

    /// <summary>
    /// Arma el mensaje y el enlace <c>wa.me</c> para un miembro. 422 si no tiene celular válido; 409 si la
    /// plantilla pide una invitación o un vencimiento que el miembro no tiene.
    /// </summary>
    [HttpGet("message")]
    [ProducesResponseType<WhatsAppMessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WhatsAppMessageResponse>> Message(
        [FromQuery] Guid memberId,
        [FromQuery] string kind,
        [FromServices] BuildWhatsAppMessageHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, kind, cancellationToken));

    /// <summary>Registra que el staff abrió el WhatsApp de un miembro con una plantilla.</summary>
    [HttpPost("contacts")]
    [ProducesResponseType<WhatsAppContactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WhatsAppContactResponse>> RegisterContact(
        RegisterWhatsAppContactRequest request,
        [FromServices] RegisterWhatsAppContactHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Por miembro (hasta 200): si tiene celular válido y el último contacto por WhatsApp.</summary>
    [HttpGet("states")]
    [ProducesResponseType<IReadOnlyList<WhatsAppMemberStateResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WhatsAppMemberStateResponse>>> States(
        [FromQuery] Guid[] memberIds,
        [FromServices] GetWhatsAppStatesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberIds, cancellationToken));
}
