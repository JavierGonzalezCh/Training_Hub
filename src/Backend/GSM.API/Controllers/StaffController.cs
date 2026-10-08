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

    /// <summary>Los perfiles predefinidos (recepción, entrenador, administrador) con los permisos que traen.</summary>
    [HttpGet("profiles")]
    [ProducesResponseType<IReadOnlyList<StaffProfileInfo>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<StaffProfileInfo>> Profiles([FromServices] ListStaffProfilesHandler handler)
        => Ok(handler.Handle());

    /// <summary>
    /// Auditoría de cambios de perfil y permisos de staff (quién, a quién, qué, cuándo), la más
    /// reciente primero; de una persona si se pasa <c>userId</c>.
    /// </summary>
    [HttpGet("audit")]
    [ProducesResponseType<IReadOnlyList<StaffAuditResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffAuditResponse>>> Audit(
        [FromQuery] Guid? userId, [FromServices] ListStaffAuditHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(userId, cancellationToken));

    /// <summary>
    /// Fija el perfil y los permisos efectivos de un staff. Los que difieren del perfil quedan como
    /// ajustes. Quitar un permiso cierra las sesiones de esa persona en este gym; agregar aplica en la
    /// próxima renovación del token.
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffResponse>> UpdatePermissions(
        Guid id,
        UpdateStaffPermissionsRequest request,
        [FromServices] UpdateStaffPermissionsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, request, cancellationToken));

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

    /// <summary>
    /// "Copiar enlace": un enlace de activación nuevo (72 h) para mandarlo por WhatsApp; si la persona ya tiene
    /// contraseña, el enlace la lleva a entrar. Queda registrado quién lo copió (historial). 409 si está desactivada.
    /// </summary>
    [HttpPost("{id:guid}/link")]
    [ProducesResponseType<AccountLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountLinkResponse>> CopyLink(
        Guid id, [FromServices] CopyStaffLinkHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

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
