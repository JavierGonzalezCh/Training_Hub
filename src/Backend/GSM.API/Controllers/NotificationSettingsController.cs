using GSM.API.Auth;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Ajustes de avisos del gym: el horario sin avisos. Solo Owner.</summary>
[ApiController]
[Route("api/notifications/settings")]
[Authorize(Policy = AuthPolicies.RequireOwner)]
public sealed class NotificationSettingsController : ControllerBase
{
    /// <summary>Horario sin avisos del gym (21:00 a 7:00 por defecto), en hora local del gym.</summary>
    [HttpGet]
    [EndpointName("GetNotificationSettings")]
    [ProducesResponseType<NotificationSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationSettingsResponse>> Get(
        [FromServices] GetNotificationSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Cambia el horario sin avisos. Aplica a Novedades, Promociones y cumpleaños; lo demás (recordatorio de
    /// clase, lista de espera, avisos al staff, seguridad) sale siempre.
    /// </summary>
    [HttpPut]
    [EndpointName("UpdateNotificationSettings")]
    [ProducesResponseType<NotificationSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<NotificationSettingsResponse>> Update(
        UpdateNotificationSettingsRequest request,
        [FromServices] UpdateNotificationSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}
