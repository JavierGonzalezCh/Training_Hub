using GSM.API.Auth;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Reglas de reserva del gym (Classes v2, C3): plazo de cancelación, ventana, máximo de reservas y penalización.</summary>
[ApiController]
[Route("api/class-settings")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class ClassSettingsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ClassSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClassSettingsResponse>> Get(
        [FromServices] GetClassSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<ClassSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ClassSettingsResponse>> Update(
        UpdateClassSettingsRequest request,
        [FromServices] UpdateClassSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}

/// <summary>Penalizaciones por inasistencia vigentes; el staff puede levantarlas con motivo.</summary>
[ApiController]
[Route("api/class-booking-blocks")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class ClassBookingBlocksController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BookingBlockResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BookingBlockResponse>>> List(
        [FromServices] ListBookingBlocksHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPost("{id:guid}/lift")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Lift(
        Guid id,
        LiftBookingBlockRequest request,
        [FromServices] LiftBookingBlockHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return NoContent();
    }
}

/// <summary>Lo que el miembro ve antes de reservar: bloqueo vigente, reservas activas y reglas.</summary>
[ApiController]
[Route("api/me/class-booking-status")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MeClassBookingStatusController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MyBookingStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MyBookingStatusResponse>> Get(
        [FromServices] GetMyBookingStatusHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
