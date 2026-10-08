namespace GSM.Application.Memberships.Public;

/// <summary>Lectura que el módulo Memberships expone a los demás módulos.</summary>
public interface IMembershipReader
{
    /// <summary>Membresía que cubre hoy (Active y con el rango de fechas conteniendo la fecha actual). <c>null</c> si no hay.</summary>
    Task<ActiveMembershipView?> GetActiveAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Ultimo dia cubierto (<c>end_date</c> maximo) entre las membresias Active del miembro: una cancelada o congelada no da días de gracia. <c>null</c> si no hay.</summary>
    Task<DateOnly?> GetLatestEndDateAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpiringMembershipView>> GetExpiringAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default);

    /// <summary>Membresías Active cuyo <c>end_date</c> ya pasó (vencidas, no renovadas).</summary>
    Task<int> GetExpiredCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<int> GetExpiringCountAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default);

    /// <summary>Gracia y ventana de "por vencer" del gym (valores por defecto si no hay fila).</summary>
    Task<MembershipSettingsView> GetSettingsAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Veredicto de acceso de un miembro hoy: estado comercial, días que le quedan, si entra y el
    /// texto del letrero (§5.3). Es lo que Attendance pide en vez de calcular la gracia él mismo.
    /// </summary>
    Task<AccessStatus> GetAccessStatusAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Estado comercial hoy de varios miembros (lista de miembros). Un miembro sin membresías es <see cref="CommercialStatus.NoPlan"/>.</summary>
    Task<IReadOnlyDictionary<Guid, CommercialStatus>> GetCommercialStatusesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids de los miembros del gym en contexto cuyo estado comercial hoy es alguno de
    /// <paramref name="statuses"/> (filtro de la lista). <see cref="CommercialStatus.NoPlan"/> no se
    /// lista aquí: quien no tiene membresías no aparece en Memberships; el llamador usa el complemento.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetMemberIdsByStatusAsync(
        IReadOnlyCollection<CommercialStatus> statuses, CancellationToken cancellationToken = default);

    /// <summary>Cuántos de <paramref name="memberIds"/> hay en cada estado comercial hoy (dashboard). Incluye <see cref="CommercialStatus.NoPlan"/>.</summary>
    Task<IReadOnlyDictionary<CommercialStatus, int>> GetStatusCountsAsync(
        Guid gymId, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>¿La membresía existe y pertenece a ese miembro? (validación al registrar un pago).</summary>
    Task<bool> BelongsToMemberAsync(
        Guid membershipId, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Miembros distintos, en toda la plataforma, con una membresía <c>Active</c> que cubre <paramref name="day"/>
    /// (<c>start_date &lt;= day &lt;= end_date</c>). Para las métricas mensuales del SaaS (sin usuario: ve todos los gyms).
    /// </summary>
    Task<int> GetCoveredMemberCountAsync(DateOnly day, CancellationToken cancellationToken = default);
}

public sealed record ActiveMembershipView(
    Guid MembershipId,
    Guid PlanId,
    string PlanName,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record ExpiringMembershipView(
    Guid MembershipId,
    Guid MemberId,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);
