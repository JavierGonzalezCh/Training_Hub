namespace GSM.Application.Attendance.Public;

/// <summary>Lectura que el módulo Attendance expone a los demás módulos (Achievements, dashboard).</summary>
public interface IAttendanceReader
{
    Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<int> GetStreakAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Días de negocio con asistencia del socio, opcionalmente desde <paramref name="from"/> (inclusive).</summary>
    Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        Guid memberId, DateOnly? from = null, CancellationToken cancellationToken = default);

    Task<int> GetTodayCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Check-ins por gym en un mes de negocio, en una sola consulta (panel de SuperAdmin).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetMonthlyCountsByGymAsync(
        int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fecha del último check-in de cada gym, en una sola consulta. Un gym que nunca tuvo check-ins
    /// no aparece: es lo que delata al gym inactivo.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastAttendanceDateByGymAsync(
        CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLastVisitAsync(Guid memberId, CancellationToken cancellationToken = default);
}
