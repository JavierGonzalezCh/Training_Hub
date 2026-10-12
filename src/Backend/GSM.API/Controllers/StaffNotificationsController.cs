using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Notifications;
using GSM.Application.Notifications.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Bandeja del staff (la campana del panel): avisos de acción del gym, que se atienden para todos, y
/// avisos informativos propios, que cada persona marca como leídos.
/// </summary>
[ApiController]
[Route("api/staff/notifications")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class StaffNotificationsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<StaffNotificationItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StaffNotificationItem>>> List(
        [FromServices] ListStaffNotificationsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool openOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await handler.HandleAsync(openOnly, page, pageSize, cancellationToken));

    /// <summary>Avisos abiertos: de acción sin atender e informativos propios sin leer.</summary>
    [HttpGet("open-count")]
    [ProducesResponseType<StaffNotificationCount>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffNotificationCount>> OpenCount(
        [FromServices] GetStaffNotificationCountHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Qué avisos quiere recibir esta persona como push en su teléfono (§8 de la especificación).</summary>
    [HttpGet("push-preferences")]
    [EndpointName("GetStaffPushPreferences")]
    [ProducesResponseType<StaffPushPreferencesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffPushPreferencesResponse>> PushPreferences(
        [FromServices] GetMyStaffPushPreferencesHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut("push-preferences")]
    [EndpointName("UpdateStaffPushPreferences")]
    [ProducesResponseType<StaffPushPreferencesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StaffPushPreferencesResponse>> UpdatePushPreferences(
        UpdateStaffPushPreferencesRequest request,
        [FromServices] UpdateMyStaffPushPreferencesHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Atiende un aviso de acción para todo el staff.</summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType<StaffNotificationItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffNotificationItem>> Resolve(
        Guid id, [FromServices] ResolveStaffNotificationHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Atiende todos los avisos de acción abiertos del gym.</summary>
    [HttpPost("resolve-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResolveAll(
        [FromServices] ResolveAllStaffNotificationsHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Marca como leído un aviso informativo propio.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(
        Guid id, [FromServices] MarkStaffNotificationReadHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);
        return NoContent();
    }
}
