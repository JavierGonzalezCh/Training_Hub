using GSM.API.Auth;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/attendance")]
public sealed class AttendanceController : ControllerBase
{
    /// <summary>Registrar entrada. Staff pasa memberId; un miembro se registra a sí mismo.</summary>
    [HttpPost("checkin")]
    [Authorize]
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

    [HttpGet("settings")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<CheckinSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckinSettingsResponse>> GetSettings(
        [FromServices] GetCheckinSettingsHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<CheckinSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckinSettingsResponse>> UpdateSettings(
        UpdateCheckinSettingsRequest request,
        [FromServices] UpdateCheckinSettingsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(request, cancellationToken));

    /// <summary>Código vigente del gym para el kiosco o el cartel. 403 si el check-in por QR está apagado.</summary>
    [HttpGet("kiosk-code")]
    [Authorize(Policy = AuthPolicies.RequireStaffOrKiosk)]
    [ProducesResponseType<KioskCodeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KioskCodeResponse>> GetKioskCode(
        [FromServices] GetKioskCodeHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Invalida el cartel impreso actual. Solo en modo fijo (409 en rotativo).</summary>
    [HttpPost("kiosk-code/regenerate")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [ProducesResponseType<KioskCodeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KioskCodeResponse>> RegenerateKioskCode(
        [FromServices] RegenerateStaticCodeHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    [HttpGet("attempts")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [ProducesResponseType<PagedResult<CheckinAttemptItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CheckinAttemptItem>>> ListAttempts(
        [FromQuery] ListCheckinAttemptsQuery query,
        [FromServices] ListCheckinAttemptsHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(query, cancellationToken));
}
