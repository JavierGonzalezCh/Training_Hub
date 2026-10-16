using GSM.API.Auth;
using GSM.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthPolicies.RequireStaff)]
public sealed class DashboardController : ControllerBase
{
    /// <summary>
    /// Panel del gym por bloques (D1). Un bloque que quien consulta no puede ver, o cuyo lector falló, viaja
    /// <c>null</c>; no recibe filtros. Cliente: <c>GetGymDashboardAsync</c>.
    /// </summary>
    [HttpGet]
    [EndpointName("GetGymDashboard")]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardResponse>> Get(
        [FromServices] GetDashboardHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
