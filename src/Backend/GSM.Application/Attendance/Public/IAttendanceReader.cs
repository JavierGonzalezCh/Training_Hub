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

    /// <summary>Check-ins del gym en un mes de negocio (para el panel de SuperAdmin).</summary>
    Task<int> GetMonthlyCountAsync(Guid gymId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>Fecha del último check-in registrado en el gym, o <c>null</c> si nunca hubo. Delata al gym inactivo.</summary>
    Task<DateOnly?> GetLastAttendanceDateAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLastVisitAsync(Guid memberId, CancellationToken cancellationToken = default);
}
