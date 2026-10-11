using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Push (N2): registro de dispositivos y datos públicos de Firebase. Cualquier sesión (miembro, staff u
/// Owner): el dispositivo es de la persona, no del gym.
/// </summary>
[ApiController]
[Route("api/push")]
[Authorize]
public sealed class PushController : ControllerBase
{
    /// <summary>
    /// Datos públicos de Firebase para que la app web obtenga su token. <c>enabled = false</c> si Firebase no
    /// está configurado: la app no ofrece activar los avisos.
    /// </summary>
    [HttpGet("config")]
    [EndpointName("GetPushConfig")]
    [ProducesResponseType<PushConfigResponse>(StatusCodes.Status200OK)]
    public ActionResult<PushConfigResponse> Config([FromServices] GetPushConfigHandler handler)
        => Ok(handler.Handle());

    /// <summary>Registra el dispositivo de la persona. Un token repetido no duplica: se reutiliza.</summary>
    [HttpPost("devices")]
    [EndpointName("RegisterPushDevice")]
    [ProducesResponseType<PushDeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PushDeviceResponse>> Register(
        RegisterPushDeviceRequest request,
        [FromServices] RegisterPushDeviceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Da de baja el dispositivo (al cerrar sesión o retirar el permiso). Idempotente.</summary>
    [HttpPost("devices/unregister")]
    [EndpointName("UnregisterPushDevice")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unregister(
        UnregisterPushDeviceRequest request,
        [FromServices] UnregisterPushDeviceHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return NoContent();
    }
}
