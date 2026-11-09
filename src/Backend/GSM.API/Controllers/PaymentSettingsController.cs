using GSM.API.Auth;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Ajustes de pagos del gym: si el efectivo se lleva por turnos de caja.</summary>
[ApiController]
[Route("api/payment-settings")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class PaymentSettingsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PaymentSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentSettingsResponse>> Get(
        [FromServices] GetPaymentSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<PaymentSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentSettingsResponse>> Update(
        UpdatePaymentSettingsRequest request,
        [FromServices] UpdatePaymentSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));
}
