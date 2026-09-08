using GSM.API.Auth;
using GSM.Application.Payments;
using GSM.Application.Payments.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/cash-register")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class CashRegisterController : ControllerBase
{
    /// <summary>Cierre de caja de un día (por defecto hoy).</summary>
    [HttpGet("daily")]
    [ProducesResponseType<DailyCashRegisterResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DailyCashRegisterResponse>> Daily(
        [FromQuery] DateOnly? date,
        [FromServices] GetDailyCashRegisterHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(date, cancellationToken));
}
