namespace GSM.Application.Classes.Contracts;

/// <summary>
/// Resultado de reservar (o reactivar) una plaza. Desde C1 reservar exige estar al día el día de la sesión:
/// <see cref="MembershipExpired"/> solo es <c>true</c> si ese día el miembro está en gracia, y
/// <see cref="AccessNotice"/> trae el letrero (por vencer o gracia) para mostrarlo.
/// </summary>
public sealed record BookingResponse(
    Guid BookingId,
    Guid SessionId,
    Guid ClassId,
    DateOnly SessionDate,
    string Status,
    bool MembershipExpired,
    string? AccessNotice = null);

public sealed record RosterEntry(
    Guid BookingId,
    Guid MemberId,
    string MemberName,
    string Status,
    DateTimeOffset? BookedAt,
    DateTimeOffset? AttendedAt);

public sealed record RosterResponse(
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    int? Capacity,
    string SessionStatus,
    int BookedCount,
    IReadOnlyList<RosterEntry> Entries);

/// <summary>
/// Marca de asistencia por el staff sobre una sesión. Los <see cref="Attended"/> pasan a
/// <c>attended</c> (creando la fila si la clase no tiene cupo); los <see cref="NoShow"/>, a
/// <c>no_show</c> (solo si tenían reserva).
/// </summary>
public sealed record MarkAttendanceRequest(
    IReadOnlyList<Guid>? Attended,
    IReadOnlyList<Guid>? NoShow);

public sealed record ClassHistoryItem(
    Guid BookingId,
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    string Status);
