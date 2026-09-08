namespace GSM.Application.Attendance.Public;

/// <summary>Lectura que el módulo Attendance expone a los demás módulos (Achievements, dashboard).</summary>
public interface IAttendanceReader
{
    Task<int> GetVisitCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<int> GetStreakAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<int> GetTodayCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLastVisitAsync(Guid memberId, CancellationToken cancellationToken = default);
}
