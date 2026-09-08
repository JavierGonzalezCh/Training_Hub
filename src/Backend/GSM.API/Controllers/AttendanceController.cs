using GSM.API.Auth;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize]
public sealed class AttendanceController : ControllerBase
{
    /// <summary>Registrar entrada. Staff pasa qrCode o memberId; un miembro se registra a sí mismo.</summary>
    [HttpPost("checkin")]
    [ProducesResponseType<CheckInResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<CheckInResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckInResponse>> CheckIn(
        CheckInRequest request, [FromServices] CheckInHandler handler, CancellationToken cancellationToken)
    {
        var (response, created) = await handler.HandleAsync(request, cancellationToken);
        return created ? StatusCode(StatusCodes.Status201Created, response) : Ok(response);
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<PagedResult<AttendanceListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AttendanceListItem>>> List(
        [FromQuery] ListAttendanceQuery query,
        [FromServices] ListAttendanceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));

    [HttpGet("peak-hours")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<IReadOnlyList<PeakHourBucket>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PeakHourBucket>>> PeakHours(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] GetPeakHoursHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(from, to, cancellationToken));
}
