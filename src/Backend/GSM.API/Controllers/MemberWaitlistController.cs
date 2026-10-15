using GSM.API.Auth;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Lista de espera de una sesión llena (Classes v2, C4), desde el lado del miembro. 403 si <c>show_classes = false</c>.
/// El orden y las ofertas las ve el staff en el roster de la sesión.
/// </summary>
[ApiController]
[Route("api/classes/{id:guid}/sessions/{date}/waitlist")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MemberWaitlistController : ControllerBase
{
    /// <summary>Anotarse en la lista de espera. 409 si la sesión aún tiene cupo, ya hay reserva o no está al día.</summary>
    [HttpPost("join")]
    [ProducesResponseType<WaitlistResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WaitlistResponse>> Join(
        Guid id, DateOnly date, [FromServices] JoinWaitlistHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, date, cancellationToken));

    /// <summary>Confirmar la oferta de cupo dentro del plazo. 409 si venció.</summary>
    [HttpPost("confirm")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingResponse>> Confirm(
        Guid id, DateOnly date, [FromServices] ConfirmWaitlistOfferHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, date, cancellationToken));

    /// <summary>Salir de la lista o rechazar la oferta (el cupo pasa al siguiente).</summary>
    [HttpPost("leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Leave(
        Guid id, DateOnly date, [FromServices] LeaveWaitlistHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, date, cancellationToken);
        return NoContent();
    }
}
