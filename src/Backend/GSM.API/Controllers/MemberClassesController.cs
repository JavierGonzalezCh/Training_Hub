using GSM.API.Auth;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Reserva de plaza por el miembro, sobre una sesión concreta. El miembro solo actúa sobre sus propios
/// datos (se resuelve del token). 403 si <c>show_classes = false</c>.
/// </summary>
[ApiController]
[Route("api/classes/{id:guid}/sessions/{date}/book")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MemberClassesController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BookingResponse>> Book(
        Guid id, DateOnly date, [FromServices] BookClassHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, date, cancellationToken));

    [HttpDelete]
    [ProducesResponseType<CancelBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CancelBookingResponse>> CancelBooking(
        Guid id, DateOnly date, [FromServices] CancelBookingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, date, cancellationToken));
}
