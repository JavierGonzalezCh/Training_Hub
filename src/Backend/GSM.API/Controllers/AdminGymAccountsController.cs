using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Las cuentas de un gym (owners, staff y miembros) vistas por el SuperAdmin: listado y "Cambiar clave".</summary>
[ApiController]
[Route("api/admin/gyms/{gymId:guid}/accounts")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminGymAccountsController : ControllerBase
{
    /// <summary>Los accesos del gym, uno por fila, filtrables por nombre o email.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<GymAccountResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<GymAccountResponse>>> List(
        Guid gymId,
        [FromQuery] string? search,
        [FromServices] ListGymAccountsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await handler.HandleAsync(gymId, search, page, pageSize, cancellationToken));

    /// <summary>
    /// Fija la contraseña de una persona del gym (soporte sin correo) y cierra todas sus sesiones. 422 si no cumple
    /// la política; 404 si no tiene acceso al gym.
    /// </summary>
    [HttpPut("{userId:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetPassword(
        Guid gymId,
        Guid userId,
        AdminSetPasswordRequest request,
        [FromServices] AdminSetPasswordHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(gymId, userId, request, cancellationToken);
        return NoContent();
    }
}
