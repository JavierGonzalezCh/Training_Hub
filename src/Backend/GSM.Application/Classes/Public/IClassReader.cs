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

    /// <summary>Clases distintas (Spinning, Yoga...) a las que el miembro asistió al menos una vez.</summary>
    Task<int> GetDistinctAttendedClassCountAsync(Guid memberId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Las sesiones de hoy del gym (calculadas + materializadas, ordenadas por hora) con reservas, cupo y lista de
    /// espera, para el dashboard del panel (D1). El cupo es el efectivo (limitado por las máquinas ligadas).
    /// </summary>
    Task<IReadOnlyList<TodaySessionView>> GetTodaySessionsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ofertas de la lista de espera que el miembro aún puede confirmar (D2: "Pendientes" del inicio): con el cupo
    /// apartado, el plazo sin vencer y la sesión sin cancelar ni pasada. La más próxima a vencer primero.
    /// </summary>
    Task<IReadOnlyList<WaitlistOfferView>> GetPendingWaitlistOffersAsync(
        Guid memberId, CancellationToken cancellationToken = default);
}

/// <summary>Una oferta de cupo por confirmar antes de <paramref name="ExpiresAt"/>.</summary>
public sealed record WaitlistOfferView(
    Guid ClassId,
    string ClassName,
    DateOnly SessionDate,
    TimeOnly StartTime,
    DateTimeOffset ExpiresAt);

/// <param name="Status"><c>Scheduled</c> o <c>Cancelled</c>.</param>
/// <param name="Capacity">Cupo efectivo; nulo = entrada libre.</param>
/// <param name="Booked">Reservas vigentes (solo se cuentan si la sesión ya existe; si nadie ha reservado vale 0).</param>
/// <param name="Waiting">Personas en la lista de espera (esperando u con oferta vigente).</param>
public sealed record TodaySessionView(
    Guid ClassId,
    string ClassName,
    TimeOnly StartTime,
    int DurationMinutes,
    string? InstructorName,
    Guid? InstructorUserId,
    string Status,
    int? Capacity,
    int Booked,
    int Waiting,
    bool OutsideOpeningHours);
