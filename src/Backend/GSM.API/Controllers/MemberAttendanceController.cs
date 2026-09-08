using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Authorize]
public sealed class MemberAttendanceController : ControllerBase
{
    [HttpGet("api/members/{memberId:guid}/attendance")]
    [ProducesResponseType<PagedResult<AttendanceListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AttendanceListItem>>> List(
        Guid memberId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] GetMemberAttendanceHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, page, pageSize, cancellationToken));

    [HttpGet("api/members/{memberId:guid}/streak")]
    [ProducesResponseType<MemberStreakResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberStreakResponse>> Streak(
        Guid memberId,
        [FromServices] GetMemberStreakHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));
}
