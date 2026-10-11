using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class PaymentsController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentResponse>> Record(
        RecordPaymentRequest request, [FromServices] RecordPaymentHandler handler, CancellationToken cancellationToken)
    {
        var payment = await handler.HandleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = payment.Id }, payment);
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<PaymentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PaymentResponse>>> List(
        [FromQuery] ListPaymentsQuery query,
        [FromServices] ListPaymentsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> Get(
        Guid id, [FromServices] GetPaymentHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, cancellationToken));

    /// <summary>
    /// Anula un pago con un asiento inverso fechado hoy. El pago original no cambia. Solo el Owner o quien tiene
    /// <c>VoidPayments</c>; el resto pide la anulación en <c>void-requests</c>.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [RequirePermission(StaffPermission.VoidPayments)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentResponse>> Void(
        Guid id, VoidPaymentRequest request, [FromServices] VoidPaymentHandler handler, CancellationToken cancellationToken)
    {
        var reversal = await handler.HandleAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = reversal.Id }, reversal);
    }

    /// <summary>Pide anular un pago, con motivo. Cualquier staff; la aprueba o rechaza quien tiene <c>VoidPayments</c>.</summary>
    [HttpPost("{id:guid}/void-requests")]
    [ProducesResponseType<VoidRequestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VoidRequestResponse>> RequestVoid(
        Guid id, RequestVoidRequest request, [FromServices] RequestVoidHandler handler, CancellationToken cancellationToken)
    {
        var created = await handler.HandleAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, created);
    }

    /// <summary>Solicitudes de anulación (pendientes primero). Owner o <c>VoidPayments</c>.</summary>
    [HttpGet("void-requests")]
    [RequirePermission(StaffPermission.VoidPayments)]
    [ProducesResponseType<PagedResult<VoidRequestResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<VoidRequestResponse>>> ListVoidRequests(
        [FromQuery] ListVoidRequestsQuery query, [FromServices] ListVoidRequestsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    /// <summary>Aprueba la solicitud: crea el asiento inverso igual que la anulación directa.</summary>
    [HttpPost("void-requests/{requestId:guid}/approve")]
    [RequirePermission(StaffPermission.VoidPayments)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentResponse>> ApproveVoidRequest(
        Guid requestId, [FromServices] ApproveVoidRequestHandler handler, CancellationToken cancellationToken)
    {
        var reversal = await handler.HandleAsync(requestId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = reversal.Id }, reversal);
    }

    /// <summary>Rechaza la solicitud sin tocar el pago.</summary>
    [HttpPost("void-requests/{requestId:guid}/reject")]
    [RequirePermission(StaffPermission.VoidPayments)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectVoidRequest(
        Guid requestId, [FromServices] RejectVoidRequestHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(requestId, cancellationToken);
        return NoContent();
    }
}
