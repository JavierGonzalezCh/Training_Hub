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
    bool OutsideOpeningHours = false);

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
