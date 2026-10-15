using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Logs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// Panel de errores (docs/GSM-logs.md §7): solo el SuperAdmin con su sesión propia. Un token de impersonación no
/// llega aquí: el filtro de suscripción le corta todo <c>/api/admin/*</c>.
/// </summary>
[ApiController]
[Route("api/admin/logs")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminLogsController : ControllerBase
{
    /// <summary>Fallas agrupadas por huella: errores, eventos fallidos, servicios externos y errores del cliente.</summary>
    [HttpGet("groups")]
    [EndpointName("ListErrorGroups")]
    [ProducesResponseType<PagedResult<ErrorGroupItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<ErrorGroupItem>>> ListGroups(
        [FromQuery] ErrorGroupsQuery query,
        [FromServices] ListErrorGroupsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    /// <summary>Un grupo con sus ocurrencias más recientes. La huella viaja en la consulta porque puede llevar símbolos.</summary>
    [HttpGet("groups/detail")]
    [EndpointName("GetErrorGroup")]
    [ProducesResponseType<ErrorGroupDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ErrorGroupDetail>> GetGroup(
        [FromQuery] string kind,
        [FromQuery] string fingerprint,
        [FromServices] GetErrorGroupHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(kind, fingerprint, cancellationToken));

    /// <summary>Cambia el estado de un grupo (<c>new</c>, <c>investigating</c>, <c>resolved</c>, <c>ignored</c>) y sus notas.</summary>
    [HttpPut("groups/status")]
    [EndpointName("SetErrorGroupStatus")]
    [ProducesResponseType<ErrorGroupItem>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ErrorGroupItem>> SetGroupStatus(
        SetErrorGroupStatusRequest request,
        [FromServices] SetErrorGroupStatusHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Busca por el código que ve el usuario (prefijo del código de rastreo). Puede devolver más de una petición.</summary>
    [HttpGet("trace/{code}")]
    [EndpointName("SearchLogsByTrace")]
    [ProducesResponseType<TraceSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TraceSearchResponse>> SearchTrace(
        string code,
        [FromServices] SearchTraceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(code, cancellationToken));

    /// <summary>Eventos de dominio cuyo manejador falló, con su estado de reintento.</summary>
    [HttpGet("event-failures")]
    [EndpointName("ListEventFailures")]
    [ProducesResponseType<PagedResult<EventFailureItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<EventFailureItem>>> ListEventFailures(
        [FromQuery] EventFailuresQuery query,
        [FromServices] ListEventFailuresHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    /// <summary>
    /// Vuelve a ejecutar el manejador. Idempotente: si ya estaba resuelto no hace nada. Siempre 200; el campo
    /// <c>outcome</c> dice si funcionó, falló otra vez o no se puede reintentar.
    /// </summary>
    [HttpPost("event-failures/{id:guid}/retry")]
    [EndpointName("RetryEventFailure")]
    [ProducesResponseType<RetryEventFailureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RetryEventFailureResponse>> RetryEventFailure(
        Guid id,
        [FromServices] RetryEventFailureHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>Reintenta todos los eventos reintentables de un grupo (mismo evento y manejador).</summary>
    [HttpPost("event-failures/retry-group")]
    [EndpointName("RetryEventFailureGroup")]
    [ProducesResponseType<RetryEventGroupResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RetryEventGroupResponse>> RetryEventFailureGroup(
        RetryEventGroupRequest request,
        [FromServices] RetryEventFailureGroupHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Descarta un evento fallido sin reintentarlo.</summary>
    [HttpPost("event-failures/{id:guid}/ignore")]
    [EndpointName("IgnoreEventFailure")]
    [ProducesResponseType<RetryEventFailureResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RetryEventFailureResponse>> IgnoreEventFailure(
        Guid id,
        [FromServices] IgnoreEventFailureHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));
}
