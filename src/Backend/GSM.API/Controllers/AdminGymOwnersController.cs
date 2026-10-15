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

    /// <summary>El contacto de facturación del gym (para cobros del SaaS y avisos de suscripción).</summary>
    [HttpGet("~/api/admin/gyms/{gymId:guid}/billing-contact")]
    [ProducesResponseType<GSM.Application.Gyms.Contracts.BillingContactResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GSM.Application.Gyms.Contracts.BillingContactResponse>> BillingContact(
        Guid gymId, [FromServices] GSM.Application.Gyms.GetBillingContactHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, cancellationToken));

    /// <summary>Agrega un Owner al gym (soporte: "el dueño perdió el acceso"). Nace sin contraseña y recibe un enlace.</summary>
    [HttpPost]
    [ProducesResponseType<GymOwnerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GymOwnerResponse>> Invite(
        Guid gymId,
        InviteOwnerRequest request,
        [FromServices] InviteOwnerHandler handler,
        CancellationToken cancellationToken)
    {
        var owner = await handler.HandleAsync(gymId, request, cancellationToken);
        return CreatedAtAction(nameof(List), new { gymId }, owner);
    }

    /// <summary>Quita a un Owner del gym (soporte: "vendió el gym"). 409 si es el último.</summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType<GymOwnerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GymOwnerResponse>> Remove(
        Guid gymId,
        Guid userId,
        [FromQuery] bool keepAsStaff,
        [FromQuery] string? staffProfile,
        [FromServices] RemoveOwnerHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(gymId, userId, keepAsStaff, staffProfile, cancellationToken));

    /// <summary>"Reenviar acceso": manda al owner por correo un enlace de activación nuevo (anula los anteriores).</summary>
    [HttpPost("{userId:guid}/resend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resend(
        Guid gymId, Guid userId, [FromServices] ResendOwnerAccessHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(gymId, userId, cancellationToken);
        return NoContent();
    }
}
