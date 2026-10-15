using GSM.API.Auth;
using GSM.Application.Identity;
using GSM.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Los Owners del gym (Configuración → Equipo). Solo un Owner. Siempre debe quedar al menos uno: quitar al
/// último es 409.
/// </summary>
[ApiController]
[Route("api/gym/owners")]
[Authorize(Policy = AuthPolicies.RequireOwner)]
public sealed class OwnersController : ControllerBase
{
    /// <summary>Los Owners del gym (activos y desactivados) y si ya activaron su cuenta.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GymOwnerResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GymOwnerResponse>>> List(
        [FromServices] ListMyOwnersHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Agrega un Owner por email: nace sin contraseña y recibe un enlace de activación. Si el email ya tiene cuenta,
    /// solo se agrega el acceso. 409 si ya es Owner o es staff del gym (usa "ascender").
    /// </summary>
    [HttpPost]
    [ProducesResponseType<GymOwnerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GymOwnerResponse>> Invite(
        InviteOwnerRequest request, [FromServices] InviteOwnerHandler handler, CancellationToken cancellationToken)
    {
        var owner = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(List), owner);
    }

    /// <summary>Asciende a un staff activo del gym a Owner: pierde su perfil, el Owner tiene todos los permisos.</summary>
    [HttpPost("{userId:guid}/promote")]
    [ProducesResponseType<GymOwnerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GymOwnerResponse>> Promote(
        Guid userId, [FromServices] PromoteStaffToOwnerHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(userId, cancellationToken));

    /// <summary>
    /// Quita a un Owner (a otro o a sí mismo): desactiva su acceso y cierra sus sesiones en este gym, o, con
    /// <c>keepAsStaff</c>, lo deja como staff con el perfil indicado (<c>staffProfile</c>, por defecto recepción).
    /// 409 si es el último Owner.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType<GymOwnerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GymOwnerResponse>> Remove(
        Guid userId,
        [FromQuery] bool keepAsStaff,
        [FromQuery] string? staffProfile,
        [FromServices] RemoveOwnerHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(userId, keepAsStaff, staffProfile, cancellationToken));
}
