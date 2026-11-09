using GSM.API.Auth;
using GSM.Application.Attendance;
using GSM.Application.Attendance.Contracts;
using GSM.Application.Common;
using GSM.Application.Identity.Contracts;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GSM.API.Controllers;

[ApiController]
[Route("api/attendance")]
public sealed class AttendanceController : ControllerBase
{
    /// <summary>
    /// Registrar entrada. Staff pasa memberId; un miembro se registra a sí mismo. Una sola entrada por día
    /// (salvo que el staff le haya habilitado entradas múltiples: hasta su máximo, con 2 horas entre una y
    /// otra): repetir es 409 (`already_checked_in`). Idempotente (A1b): un escaneo repetido dentro de 2 minutos
    /// devuelve la misma entrada con 200 en vez de crear otra.
    /// </summary>
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

    /// <summary>
    /// ¿Debe la app pedir la ubicación del teléfono antes de registrar la entrada? Solo con el QR fijo y si el gym
    /// tiene coordenadas (A6). Lo consulta la app del miembro antes de llamar a `checkin`.
    /// </summary>
    [HttpGet("checkin/location-policy")]
    [Authorize]
    [EndpointName("GetCheckinLocationPolicy")]
    [ProducesResponseType<CheckinLocationPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CheckinLocationPolicyResponse>> GetCheckinLocationPolicy(
        [FromServices] GetCheckinLocationPolicyHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Tarjeta de recepción: estado del miembro antes de registrar su entrada. No cambia datos.</summary>
    [HttpGet("reception/{memberId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [EndpointName("ReceptionCard")]
    [ProducesResponseType<ReceptionCardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceptionCardResponse>> ReceptionCard(
        Guid memberId, [FromServices] GetReceptionCardHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    /// <summary>Entradas por día de un miembro (ficha). Sin regla vigente rige una por día.</summary>
    [HttpGet("entry-rules/{memberId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [EndpointName("GetEntryRule")]
    [ProducesResponseType<EntryRuleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntryRuleResponse>> GetEntryRule(
        Guid memberId, [FromServices] GetEntryRuleHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, cancellationToken));

    /// <summary>Habilita o cambia las entradas múltiples (2 a 5 por día, motivo obligatorio). Owner o cualquier staff.</summary>
    [HttpPut("entry-rules/{memberId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [EndpointName("SetEntryRule")]
    [ProducesResponseType<EntryRuleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EntryRuleResponse>> SetEntryRule(
        Guid memberId, SetEntryRuleRequest request, [FromServices] SetEntryRuleHandler handler,
        CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(memberId, request, cancellationToken));

    /// <summary>Quita las entradas múltiples: vuelve a una por día. Idempotente.</summary>
    [HttpDelete("entry-rules/{memberId:guid}")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [EndpointName("RemoveEntryRule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveEntryRule(
        Guid memberId, [FromServices] RemoveEntryRuleHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(memberId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Anula una entrada (borrado lógico con motivo) de los últimos 7 días. Owner o staff con el permiso
    /// `ManageAttendance`. Deja libre el día, la racha se recalcula y se publica `CheckInVoided`.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [RequirePermission(StaffPermission.ManageAttendance)]
    [EndpointName("VoidCheckIn")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> VoidCheckIn(
        Guid id, VoidCheckInRequest request, [FromServices] VoidCheckInHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, request, cancellationToken);
        return NoContent();
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

    /// <summary>
    /// Cuánta gente hay en el gym ahora (estimación: entradas no anuladas de las últimas 2 horas, por persona) y
    /// qué porcentaje del aforo es. Sin aforo configurado solo trae el número de personas.
    /// </summary>
    [HttpGet("occupancy")]
    [Authorize(Policy = AuthPolicies.RequireStaff)]
    [EndpointName("GetOccupancy")]
    [ProducesResponseType<OccupancyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OccupancyResponse>> Occupancy(
        [FromServices] GetOccupancyHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

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

    /// <summary>Código vigente del gym para el kiosco. Solo la cuenta de dispositivo (403 al staff). 403 también si el check-in por QR y la entrada con documento están ambos apagados (con solo el documento, `QrDisabled` = true y sin código).</summary>
    [HttpGet("kiosk-code")]
    [Authorize(Policy = AuthPolicies.RequireKiosk)]
    [ProducesResponseType<KioskCodeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KioskCodeResponse>> GetKioskCode(
        [FromServices] GetKioskCodeHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>Nombre, logo y colores del gym del kiosco, para mostrarlos sobre el QR. Solo la cuenta de dispositivo (403 al staff).</summary>
    [HttpGet("kiosk/branding")]
    [Authorize(Policy = AuthPolicies.RequireKiosk)]
    [EndpointName("KioskBranding")]
    [ProducesResponseType<PublicGymBrandingResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicGymBrandingResponse>> KioskBranding(
        [FromServices] GetKioskBrandingHandler handler, CancellationToken cancellationToken)
        => Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Entrada con el documento digitado en el kiosco (A4). Solo la cuenta de dispositivo (403 al staff) y solo si
    /// el gym la encendió (403). Mismas reglas que el QR. Devuelve solo nombre de pila y foto. Todo rechazo es un
    /// 409 idéntico (sin decir si el documento existe ni el motivo); cinco fallos seguidos bloquean el teclado
    /// un minuto (429 con `Retry-After`).
    /// </summary>
    [HttpPost("kiosk/document-entry")]
    [Authorize(Policy = AuthPolicies.RequireKiosk)]
    [EndpointName("KioskDocumentEntry")]
    [ProducesResponseType<KioskDocumentEntryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<KioskDocumentEntryResponse>> KioskDocumentEntry(
        KioskDocumentEntryRequest request, [FromServices] KioskDocumentEntryHandler handler,
        CancellationToken cancellationToken)
    {
        var outcome = await handler.HandleAsync(request, cancellationToken);
        if (outcome.LockedUntil is { } until)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds));
            Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Problem(
                title: "Teclado bloqueado",
                detail: "Demasiados intentos. Espera un minuto o acércate a recepción.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return StatusCode(StatusCodes.Status201Created, outcome.Entry);
    }

    /// <summary>Código fijo vigente para imprimir el cartel. Solo Owner. 409 si el gym no está en modo de cartel fijo; 403 si el check-in con QR está apagado.</summary>
    [HttpGet("static-code")]
    [Authorize(Policy = AuthPolicies.RequireOwner)]
    [EndpointName("GetStaticCode")]
    [ProducesResponseType<KioskCodeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<KioskCodeResponse>> GetStaticCode(
        [FromServices] GetStaticCodeHandler handler, CancellationToken cancellationToken)
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
