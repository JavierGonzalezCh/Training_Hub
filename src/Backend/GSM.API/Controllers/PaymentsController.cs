using GSM.API.Auth;
using GSM.Application.Common;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
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

    /// <summary>Anula un pago con un asiento inverso fechado hoy. El pago original no cambia.</summary>
    [HttpPost("{id:guid}/void")]
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
}
