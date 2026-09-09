using GSM.API.Auth;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Cobros del SaaS de toda la plataforma. Solo lectura: los pagos son inmutables.</summary>
[ApiController]
[Route("api/admin/payments")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminPaymentsController : ControllerBase
{
    /// <summary>Todos los cobros del periodo (por defecto, el mes en curso). Agrupado por día de negocio.</summary>
    [HttpGet]
    [ProducesResponseType<PlatformPaymentsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlatformPaymentsResponse>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] ListPlatformPaymentsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));
}
