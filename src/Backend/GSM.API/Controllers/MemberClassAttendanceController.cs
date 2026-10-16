using GSM.API.Auth;
using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

/// <summary>
/// "Estoy en clase" (Classes v2, C7): el miembro marca su propia asistencia. Exige haber registrado su entrada al
/// gym hoy y estar entre 15 minutos antes y 15 minutos después del inicio. 403 si <c>show_classes = false</c>.
/// </summary>
[ApiController]
[Route("api/classes/{id:guid}/sessions/{date}/attend")]
[Authorize(Policy = AuthPolicies.RequireMember)]
public sealed class MemberClassAttendanceController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SelfAttendanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SelfAttendanceResponse>> Attend(
        Guid id, DateOnly date, [FromServices] MarkOwnAttendanceHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(id, date, cancellationToken));
}
