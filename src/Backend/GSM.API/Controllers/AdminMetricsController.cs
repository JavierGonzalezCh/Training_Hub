using GSM.API.Auth;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Evolución mensual del SaaS: MRR, cobrado, gyms, churn, miembros y entradas (últimos 12 meses).</summary>
[ApiController]
[Route("api/admin/metrics")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminMetricsController : ControllerBase
{
    [HttpGet("monthly")]
    [ProducesResponseType<MonthlyMetricsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MonthlyMetricsResponse>> GetMonthly(
        [FromServices] ListMonthlyMetricsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
