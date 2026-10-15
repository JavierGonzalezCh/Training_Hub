namespace GSM.Application.Classes.Public;

/// <summary>Próxima reserva del miembro (para la home de su app).</summary>
public sealed record UpcomingBookingView(
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    string? InstructorName);

/// <summary>Lectura que el módulo Classes expone a los demás módulos (Achievements, dashboard).</summary>
public interface IClassReader
{
    /// <summary>Nº de sesiones de clase a las que el miembro asistió (métrica del logro <c>classes</c>).</summary>
    Task<int> GetAttendedCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UpcomingBookingView>> GetUpcomingBookingsAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Nº de sesiones (calculadas + materializadas no canceladas) que ocurren hoy en el gym.</summary>
    Task<int> GetTodaySessionCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids de los miembros del gym en contexto con una reserva vigente o una asistencia en alguna de esas
    /// clases en una sesión desde <paramref name="since"/> (inclusive). Filtro "clase" de la audiencia de
    /// un anuncio (CO2: últimos 30 días).
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetMemberIdsByClassesAsync(
        IReadOnlyCollection<Guid> classIds, DateOnly since, CancellationToken cancellationToken = default);

    /// <summary>Nombre de la clase (para los avisos de Comunicaciones). Nulo si no existe.</summary>
    Task<string?> GetClassNameAsync(Guid classId, CancellationToken cancellationToken = default);
}
