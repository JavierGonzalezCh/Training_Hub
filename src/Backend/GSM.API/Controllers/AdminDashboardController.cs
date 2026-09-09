using GSM.API.Auth;
using GSM.Application.SuperAdmin;
using GSM.Application.SuperAdmin.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>Panel de SuperAdmin: cartera, ingresos, uso por gym y totales de plataforma.</summary>
[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Policy = AuthPolicies.RequireSuperAdmin)]
public sealed class AdminDashboardController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdminDashboardResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminDashboardResponse>> Get(
        [FromServices] GetAdminDashboardHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
