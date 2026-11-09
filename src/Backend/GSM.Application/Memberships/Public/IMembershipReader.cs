namespace GSM.Application.Memberships.Public;

/// <summary>Lectura que el módulo Memberships expone a los demás módulos.</summary>
public interface IMembershipReader
{
    /// <summary>Membresía que cubre hoy (Active y con el rango de fechas conteniendo la fecha actual). <c>null</c> si no hay.</summary>
    Task<ActiveMembershipView?> GetActiveAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>¿El gym tiene fila de ajustes de membresías? La deja la plantilla de su tipo al crearlo (marca de «plantilla aplicada»).</summary>
    Task<bool> HasSettingsAsync(Guid gymId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Ultimo dia cubierto (<c>end_date</c> maximo) entre las membresias Active del miembro: una cancelada o congelada no da días de gracia. <c>null</c> si no hay.</summary>
    Task<DateOnly?> GetLatestEndDateAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpiringMembershipView>> GetExpiringAsync(
        Guid gymId, int withinDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membresías Active del gym dentro de la ventana de los recordatorios de vencimiento (CO7): su
    /// <c>end_date</c> cae entre <c>hoy - GraceDays</c> y <c>hoy + daysBefore</c> (el último día de la
    /// gracia es <c>end_date + GraceDays</c>). <c>DaysLeft</c> es negativo si ya venció.
    /// </summary>
    Task<IReadOnlyList<ReminderMembershipView>> GetReminderCandidatesAsync(
        Guid gymId, int daysBefore, CancellationToken cancellationToken = default);

    /// <summary>
    /// MS2-3: membresías no canceladas que siguen vigentes o están por empezar (<c>end_date &gt;= hoy</c>) y tienen algo
    /// que cobrar. El dashboard las cruza con lo pagado (Payments) para mostrar las pendientes de pago.
    /// </summary>
    Task<IReadOnlyList<ChargeableMembershipView>> GetChargeableAsync(
        Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membresías Active que terminaron en los últimos <paramref name="withinDays"/> días (de ayer hacia atrás), con
    /// <c>DaysLeft</c> negativo. Incluye las que aún están en gracia: quien llama cruza con el estado comercial del miembro.
    /// </summary>
    Task<IReadOnlyList<ReminderMembershipView>> GetRecentlyEndedAsync(
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

    /// <summary>
    /// Igual que <see cref="GetAccessStatusAsync"/> pero a una fecha de negocio dada (por ejemplo, el día de una
    /// clase que se quiere reservar): ¿tendrá cobertura, estará por vencer o en gracia ese día?
    /// </summary>
    Task<AccessStatus> GetAccessStatusOnAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Miembros del gym que cuentan hoy para el límite de su plan del SaaS: los que tienen una membresía
    /// que los cubre (al día, por vencer, en gracia o congelados; ver <see cref="CommercialStatusExtensions"/>).
    /// Los miembros desactivados no cuentan aunque conserven una membresía vigente (S2-3).
    /// Por gym explícito, para que SuperAdmin pueda preguntar por cualquiera.
    /// </summary>
    Task<int> GetCountedMemberCountAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids de los miembros del gym en contexto con una membresía Active que cubre hoy (<c>start_date &lt;= hoy
    /// &lt;= end_date</c>) en alguno de esos planes. Filtro "plan" de la audiencia de un anuncio (CO2).
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetMemberIdsByPlansAsync(
        IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Días de negocio cubiertos por las membresías del miembro hasta hoy (antigüedad, L2): sin las canceladas,
    /// sin los días congelados y sin contar lo pagado por adelantado que aún no se vive. Lo vuelve meses
    /// quien lo pide (días / 30).
    /// </summary>
    Task<int> GetCoveredDaysAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Lo mismo en bloque (retroactivos): días cubiertos de cada miembro pedido; quien no tiene membresías vale 0.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetCoveredDaysAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// De los miembros pedidos, los que ya tienen una membresía pagada: no cancelada y con precio acordado
    /// mayor que cero (una cortesía de precio 0 no cuenta). Para los referidos (L2).
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetMemberIdsWithPaidMembershipAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);

    /// <summary>¿La membresía existe y pertenece a ese miembro? (validación al registrar un pago).</summary>
    Task<bool> BelongsToMemberAsync(
        Guid membershipId, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Planes activos del gym (onboarding G5: el paso "Planes" pide al menos uno).</summary>
    Task<int> GetActivePlanCountAsync(Guid gymId, CancellationToken cancellationToken = default);

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

public sealed record ChargeableMembershipView(
    Guid MembershipId, Guid MemberId, string PlanName, decimal TotalToCharge, DateOnly StartDate);

public sealed record ExpiringMembershipView(
    Guid MembershipId,
    Guid MemberId,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft);

/// <param name="DaysLeft">Dias hasta <c>EndDate</c> (0 = vence hoy; negativo = ya vencio, en gracia).</param>
/// <param name="GraceDays">Dias de gracia vigentes del gym: el ultimo dia para renovar es <c>EndDate + GraceDays</c>.</param>
public sealed record ReminderMembershipView(
    Guid MembershipId,
    Guid MemberId,
    string PlanName,
    DateOnly EndDate,
    int DaysLeft,
    int GraceDays);
