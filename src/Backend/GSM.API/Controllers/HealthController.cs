using GSM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController(GsmDbContext dbContext) : ControllerBase
{
    /// <summary>Liveness: el proceso responde.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "GSM.API" });

    /// <summary>Readiness: hay conexión con PostgreSQL.</summary>
    [HttpGet("db")]
    public async Task<IActionResult> GetDb(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? Ok(new { status = "ok", database = "reachable" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "error", database = "unreachable" });
    }
}
