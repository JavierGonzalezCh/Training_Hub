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
    string? MyBookingStatus = null);

public sealed record ScheduleResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ScheduleEntry> Entries);

public sealed record CancelSessionRequest(string? Reason);
