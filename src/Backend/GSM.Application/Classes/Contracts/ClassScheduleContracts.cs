namespace GSM.Application.Classes.Contracts;

/// <summary>
/// Una ocurrencia en el horario proyectado: puede venir de una sesión ya materializada
/// (<see cref="IsMaterialized"/> = true, y entonces manda) o del cálculo de la recurrencia.
/// </summary>
public sealed record ScheduleEntry(
    Guid ClassId,
    string ClassName,
    string? Description,
    string? InstructorName,
    int? Capacity,
    DateOnly SessionDate,
    TimeOnly StartTime,
    int DurationMinutes,
    string Status,
    string? CancellationReason,
    bool IsMaterialized,
    Guid? SessionId,
    /// <summary>Estado de la reserva del miembro que consulta (solo en el horario del miembro); null si no ha reservado o es vista de staff.</summary>
    string? MyBookingStatus = null,
    /// <summary>
    /// C1: la ocurrencia cae fuera del horario de atención del gym ese día (festivo con horario corto,
    /// cierre por excepción o cambio de horario). No se puede reservar ni marcar asistencia; la UI dice "Gym cerrado".
    /// </summary>
    bool OutsideOpeningHours = false,
    /// <summary>C2: el entrenador de la lista que da la clase; nulo = "Otro" o sin instructor.</summary>
    Guid? InstructorUserId = null,
    /// <summary>C4 (solo horario del miembro): la sesión con cupo está llena (reservas + cupos ofrecidos); la UI ofrece la lista de espera.</summary>
    bool? IsFull = null,
    /// <summary>C4 (solo horario del miembro): <c>Waiting</c> u <c>Offered</c> si el miembro está en la lista de espera de la sesión.</summary>
    string? MyWaitlistStatus = null,
    /// <summary>C4: su lugar en la lista (1 = el primero).</summary>
    int? MyWaitlistPosition = null,
    /// <summary>C4: hora límite para confirmar la oferta de cupo.</summary>
    DateTimeOffset? MyOfferExpiresAt = null);

public sealed record ScheduleResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ScheduleEntry> Entries);

public sealed record CancelSessionRequest(string? Reason);

/// <summary>
/// Una clase activa que ya no cabe en el horario del gym (el gym cambió su horario). El staff la mueve.
/// <see cref="DaysOutside"/> son los días de la semana (<c>Monday</c>…) de una serie que quedan fuera;
/// vacío en una clase puntual, que usa <see cref="ScheduledDate"/>.
/// </summary>
public sealed record ClassOutsideHoursItem(
    Guid ClassId,
    string Name,
    TimeOnly StartTime,
    int DurationMinutes,
    bool IsRecurring,
    IReadOnlyList<string> DaysOutside,
    DateOnly? ScheduledDate);
