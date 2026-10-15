using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Caja por turno con arqueo (Payments v2 §4).</summary>
[ApiController]
[Route("api/cash-shifts")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class CashShiftsController : ControllerBase
{
    /// <summary>La barra de la caja: el turno abierto (si lo hay) con su efectivo esperado y la base sugerida.</summary>
    [HttpGet("current")]
    [ProducesResponseType<CashDrawerStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CashDrawerStatusResponse>> Current(
        [FromServices] GetCashDrawerStatusHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Abre la caja del turno con una base en efectivo. 409 si ya hay una abierta.</summary>
    [HttpPost("open")]
    [ProducesResponseType<CashShiftResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CashShiftResponse>> Open(
        OpenCashShiftRequest request, [FromServices] OpenCashShiftHandler handler, CancellationToken cancellationToken)
    {
        var shift = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Report), new { id = shift.Id }, shift);
    }

    /// <summary>Retiro o ingreso de efectivo a mitad de turno. Quien abrió la caja o el Owner.</summary>
    [HttpPost("current/movements")]
    [ProducesResponseType<CashMovementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CashMovementResponse>> Movement(
        CashMovementRequest request, [FromServices] RecordCashMovementHandler handler, CancellationToken cancellationToken)
    {
        var movement = await handler.HandleAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, movement);
    }

    /// <summary>Cierra la caja con el arqueo y devuelve el reporte del turno. Sin nota, 422 si hay diferencia.</summary>
    [HttpPost("current/close")]
    [ProducesResponseType<CashShiftReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CashShiftReportResponse>> Close(
        CloseCashShiftRequest request, [FromServices] CloseCashShiftHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Turnos con sus diferencias, filtrables por persona y fecha. Owner o <c>ViewFinances</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CashShiftResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<CashShiftResponse>>> List(
        [FromQuery] ListCashShiftsQuery query, [FromServices] ListCashShiftsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    /// <summary>Reporte imprimible de un turno. Quien lo abrió o lo cerró; el Owner o <c>ViewFinances</c>, cualquiera.</summary>
    [HttpGet("{id:guid}/report")]
    [ProducesResponseType<CashShiftReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CashShiftReportResponse>> Report(
        Guid id, [FromServices] GetCashShiftReportHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));
}
