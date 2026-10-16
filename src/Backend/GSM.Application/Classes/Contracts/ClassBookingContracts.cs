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
    IReadOnlyList<RosterEntry> Entries,
    /// <summary>C4: la lista de espera en su orden (esperando y con oferta en curso); nula si la sesión no tiene.</summary>
    IReadOnlyList<WaitlistRosterEntry>? Waitlist = null,
    /// <summary>C6 (solo clases ligadas a máquinas): cupo efectivo de la sesión = menor entre el cupo y las máquinas disponibles ese día.</summary>
    int? EffectiveCapacity = null,
    /// <summary>C6: unidades de las máquinas ligadas que sirven ese día ("18 reservas y 17 bicicletas disponibles").</summary>
    int? AvailableMachines = null,
    /// <summary>C6: unidades de las máquinas ligadas que siguen en el inventario.</summary>
    int? LinkedMachines = null);

/// <summary>Una persona de la lista de espera. <see cref="Position"/> es su lugar en la lista (1 = la primera).</summary>
public sealed record WaitlistRosterEntry(
    Guid EntryId,
    Guid MemberId,
    string MemberName,
    int Position,
    string Status,
    DateTimeOffset? OfferExpiresAt);

/// <summary>
/// Estado del miembro en la lista de espera de una sesión (C4). <see cref="Status"/> es <c>Waiting</c> u <c>Offered</c>;
/// con oferta, <see cref="OfferExpiresAt"/> es la hora límite para confirmar.
/// </summary>
public sealed record WaitlistResponse(
    Guid EntryId,
    Guid SessionId,
    Guid ClassId,
    DateOnly SessionDate,
    string Status,
    int Position,
    DateTimeOffset? OfferExpiresAt);

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

/// <summary>
/// C7: resultado de "Estoy en clase". <see cref="Status"/> es <c>Attended</c>; <see cref="AttendedAt"/> es el
/// instante en que quedó marcada (el de la primera vez si el miembro insiste).
/// </summary>
public sealed record SelfAttendanceResponse(
    Guid BookingId,
    Guid SessionId,
    Guid ClassId,
    DateOnly SessionDate,
    string Status,
    DateTimeOffset? AttendedAt);
