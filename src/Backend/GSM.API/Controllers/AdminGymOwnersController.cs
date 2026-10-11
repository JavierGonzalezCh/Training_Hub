using GSM.API.Auth;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Los Owners de un gym (cuentas de Identity) vistos por el SuperAdmin: estado de activación y enlace.</summary>
[ApiController]
[Route("api/admin/gyms/{gymId:guid}/owners")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminGymOwnersController : ControllerBase
{
    /// <summary>Los owners del gym y si ya activaron su cuenta (definieron su contraseña).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GymOwnerResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GymOwnerResponse>>> List(
        Guid gymId, [FromServices] ListGymOwnersHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, cancellationToken));

    /// <summary>
    /// "Copiar enlace": un enlace de activación nuevo (72 h) del owner para mandarlo por WhatsApp; si ya tiene
    /// contraseña, el enlace lo lleva a entrar. Queda registrado quién lo copió.
    /// </summary>
    [HttpPost("{userId:guid}/link")]
    [ProducesResponseType<AccountLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountLinkResponse>> CopyLink(
        Guid gymId, Guid userId, [FromServices] CopyOwnerLinkHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, userId, cancellationToken));
}
