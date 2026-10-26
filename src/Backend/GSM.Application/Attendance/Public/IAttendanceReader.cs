namespace GSM.Application.Attendance.Public;

/// <summary>Lectura que el módulo Attendance expone a los demás módulos (Achievements, dashboard).</summary>
/// <param name="RoutineEntry">Sin registro en la puerta: el miembro cuenta su día por entrenamiento (A8).</param>
/// <param name="FirstLog">Además, el día cuenta al registrar el primer ejercicio y no al tocar un botón (T4).</param>
public sealed record RoutineEntryMode(bool RoutineEntry, bool FirstLog);

public interface IAttendanceReader
{
    Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Días con entrada de cada miembro pedido, en una sola consulta (logros retroactivos, L1). Cuenta días, no
    /// entradas, y no cuenta las anuladas. Quien no tiene entradas no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetVisitCountsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Días con entrada válida de cada miembro pedido dentro de <c>[from, toExclusive)</c> (días de negocio), con el
    /// día en que llegó a esa cantidad (el último día con entrada), para el ranking mensual (L6). Mismo criterio que
    /// las visitas: cuenta días, no entradas, y no cuenta las anuladas. Quien no entró en el rango no aparece.
    /// </summary>
    Task<IReadOnlyList<MemberDaysInRange>> GetDayCountsInRangeAsync(
        IReadOnlyCollection<Guid> memberIds, DateOnly from, DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<int> GetStreakAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Días de negocio con asistencia del miembro, opcionalmente desde <paramref name="from"/> (inclusive).</summary>
    Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        Guid memberId, DateOnly? from = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Días del rango (inclusive) en que el miembro tiene entradas válidas pero <b>ninguna cuenta</b> para la racha:
    /// todas con QR fijo fuera del radio del gym o sin ubicación (A6). Para explicárselo en "Mi progreso" (L7).
    /// </summary>
    Task<IReadOnlyList<UncountedEntryDay>> GetUncountedEntryDaysAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>A1b-6: <b>personas distintas</b> con una entrada válida hoy (no entradas).</summary>
    Task<int> GetTodayCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Personas distintas con entrada válida por gym en un mes de negocio (A1b-6), en una sola consulta (panel de SuperAdmin).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetMonthlyCountsByGymAsync(
        int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fecha del último check-in de cada gym, en una sola consulta. Un gym que nunca tuvo check-ins
    /// no aparece: es lo que delata al gym inactivo.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastAttendanceDateByGymAsync(
        CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLastVisitAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// La primera entrada válida (no anulada) del miembro en el día de negocio de hoy: "Entraste hoy a las 6:12"
    /// (D2). Nulo si todavía no ha entrado hoy.
    /// </summary>
    Task<DateTimeOffset?> GetFirstEntryTodayAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A8: el gym no tiene ningún registro de entrada en la puerta (check-in con QR y entrada con documento
    /// apagados). Entonces el miembro cuenta su día —racha, visitas y logros— al iniciar su entrenamiento.
    /// </summary>
    /// <summary>¿El gym tiene fila de ajustes de check-in? La deja la plantilla de su tipo al crearlo (marca de «plantilla aplicada»).</summary>
    Task<bool> HasCheckinSettingsAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<bool> IsRoutineEntryAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <see cref="IsRoutineEntryAsync"/> y <see cref="IsFirstLogEntryAsync"/> en una sola lectura de flags y ajustes
    /// (los pide <c>GET /api/auth/me</c> en cada arranque).
    /// </summary>
    Task<RoutineEntryMode> GetRoutineEntryModeAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Los miembros del gym entrenan fuera de la sede? (<c>remote_training</c>, T4). Sin fila de ajustes, no. Lo lee el
    /// onboarding para saber qué pasos son obligatorios (T5).
    /// </summary>
    Task<bool> IsRemoteTrainingAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿El día cuenta al registrar el primer ejercicio y no al tocar un botón? Solo con <see cref="IsRoutineEntryAsync"/>
    /// y el ajuste <c>first_log</c> (T4).
    /// </summary>
    Task<bool> IsFirstLogEntryAsync(Guid gymId, CancellationToken cancellationToken = default);

    // --- Lecturas del dashboard del panel (D1). Siempre sobre el gym en contexto. ---

    /// <summary>Personas adentro ahora (estimación), aforo y % del aforo (A5).</summary>
    Task<OccupancyView> GetOccupancyAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Intentos de entrada rechazados del día de negocio de hoy (los más recientes primero, hasta
    /// <paramref name="limit"/>), con el total del día y cuántos siguen sin verse.
    /// </summary>
    Task<RejectedTodayView> GetRejectedTodayAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Las últimas entradas válidas de hoy (más reciente primero), hasta <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<RecentEntryView>> GetRecentEntriesTodayAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entradas por hora de negocio de los últimos <paramref name="days"/> días (hoy incluido). Solo las horas con
    /// al menos una entrada; el llamador rellena los huecos.
    /// </summary>
    Task<IReadOnlyList<HourlyCountView>> GetHourlyCountsAsync(int days, CancellationToken cancellationToken = default);
}

/// <param name="Level"><c>calm</c>, <c>moderate</c> o <c>full</c>; nulo si el gym no configuró aforo.</param>
public sealed record OccupancyView(int PeopleInside, int? Capacity, int? Percent, string? Level, int AssumedStayMinutes);

public sealed record RejectedAttemptView(
    Guid AttemptId, Guid MemberId, DateTimeOffset AttemptedAt, int AttemptCount, string Reason, bool Seen);

/// <param name="Total">Intentos rechazados hoy (todos).</param>
/// <param name="Unseen">Los que nadie del staff ha atendido todavía.</param>
public sealed record RejectedTodayView(int Total, int Unseen, IReadOnlyList<RejectedAttemptView> Items);

public sealed record RecentEntryView(Guid AttendanceId, Guid MemberId, DateTimeOffset CheckedInAt, string Method);

public sealed record HourlyCountView(int Hour, int Count);

/// <summary>Días con entrada de un miembro en un rango y el último de esos días (cuándo llegó a esa cantidad).</summary>
public sealed record MemberDaysInRange(Guid MemberId, int Days, DateOnly LastDay);

/// <summary>Un día con entrada que no contó. <c>Outside</c>: estaba fuera del radio; si no, no compartió su ubicación.</summary>
public sealed record UncountedEntryDay(DateOnly Date, bool Outside);
