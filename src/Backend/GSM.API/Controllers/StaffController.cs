using GSM.API.Auth;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Gestión de las cuentas de staff del gym. Solo el owner.</summary>
[ApiController]
[Route("api/gym/staff")]
[Authorize(Policy = AuthPolicies.RequireOwner)]
public sealed class StaffController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StaffResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffResponse>>> List(
        [FromServices] ListStaffHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Crea la cuenta sin contraseña y envía por email el código para definirla. 409 si el email ya existe.</summary>
    [HttpPost]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffResponse>> Create(
        CreateStaffRequest request,
        [FromServices] CreateStaffHandler handler,
        CancellationToken cancellationToken)
    {
        var staff = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(List), staff);
    }

    /// <summary>Desactiva la cuenta (no la borra) y revoca sus refresh tokens. Idempotente.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        Guid id, [FromServices] DeactivateStaffHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Reactiva una cuenta desactivada. Idempotente.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffResponse>> Reactivate(
        Guid id, [FromServices] ReactivateStaffHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));
}
