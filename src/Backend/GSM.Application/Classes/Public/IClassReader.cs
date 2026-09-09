namespace GSM.Application.Classes.Public;

/// <summary>Próxima reserva del socio (para la home de su app).</summary>
public sealed record UpcomingBookingView(
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    string? InstructorName);

/// <summary>Lectura que el módulo Classes expone a los demás módulos (Achievements, dashboard).</summary>
public interface IClassReader
{
    /// <summary>Nº de sesiones de clase a las que el socio asistió (métrica del logro <c>classes</c>).</summary>
    Task<int> GetAttendedCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UpcomingBookingView>> GetUpcomingBookingsAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Nº de sesiones (calculadas + materializadas no canceladas) que ocurren hoy en el gym.</summary>
    Task<int> GetTodaySessionCountAsync(Guid gymId, CancellationToken cancellationToken = default);
}
