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
    /// <summary>Panel del gym: una sola llamada, un solo response.</summary>
    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardResponse>> Get(
        [FromServices] GetDashboardHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));
}
