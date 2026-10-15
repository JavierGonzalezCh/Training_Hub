namespace GSM.Application.Classes.Contracts;

/// <summary>Reglas de reserva del gym (Classes v2, C3). Sin fila guardada devuelve los valores por defecto.</summary>
public sealed record ClassSettingsResponse(
    int CancelDeadlineHours,
    int NoShowLimit,
    int NoShowWindowDays,
    int NoShowBlockDays,
    int BookingWindowDays,
    int MaxActiveBookings);

public sealed record UpdateClassSettingsRequest(
    int CancelDeadlineHours,
    int NoShowLimit,
    int NoShowWindowDays,
    int NoShowBlockDays,
    int BookingWindowDays,
    int MaxActiveBookings);

/// <summary>
/// Lo que el miembro necesita saber para reservar sin sorpresas: si está bloqueado y hasta cuándo, cuántas
/// reservas activas lleva, y las reglas que se le advierten antes de confirmar (plazo y ventana).
/// </summary>
public sealed record MyBookingStatusResponse(
    DateOnly? BlockedUntil,
    int ActiveBookings,
    int MaxActiveBookings,
    int CancelDeadlineHours,
    int BookingWindowDays);

/// <summary>
/// Resultado de cancelar una reserva. <see cref="CountedAsNoShow"/>: se canceló pasado el plazo y cuenta
/// como inasistencia; <see cref="BlockedUntil"/> viene si con esta cancelación se alcanzó el límite.
/// </summary>
public sealed record CancelBookingResponse(bool CountedAsNoShow, DateOnly? BlockedUntil);

/// <summary>Un miembro con la penalización vigente, para que el staff pueda levantarla.</summary>
public sealed record BookingBlockResponse(
    Guid Id,
    Guid MemberId,
    string MemberName,
    DateOnly BlockedUntil,
    int StrikeCount,
    DateTimeOffset CreatedAt);

public sealed record LiftBookingBlockRequest(string Reason);
