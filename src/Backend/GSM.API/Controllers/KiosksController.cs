using GSM.API.Auth;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Cuentas de dispositivo del kiosco (Identity §8). Solo el owner.</summary>
[ApiController]
[Route("api/gym/kiosks")]
[Authorize(Policy = AuthPolicies.RequireOwner)]
public sealed class KiosksController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<KioskDeviceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<KioskDeviceResponse>>> List(
        [FromServices] ListKiosksHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Crea el kiosco y devuelve su código de emparejamiento de 8 caracteres (vence en 10 minutos; solo se muestra esta vez).</summary>
    [HttpPost]
    [ProducesResponseType<KioskCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<KioskCreatedResponse>> Create(
        CreateKioskRequest request, [FromServices] CreateKioskHandler handler, CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(List), created);
    }

    /// <summary>Revoca el kiosco: cierra su sesión y anula el código pendiente. Idempotente.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<KioskDeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KioskDeviceResponse>> Revoke(
        Guid id, [FromServices] RevokeKioskHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));
}
