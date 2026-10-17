using GSM.Domain.Memberships;

namespace GSM.Application.Memberships.Abstractions;

/// <summary>Acceso de escritura/consulta del módulo Memberships. Sin <c>SaveChanges</c>.</summary>
public interface IMembershipRepository
{
    // Planes
    Task<MembershipPlan?> GetPlanByIdAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MembershipPlan>> ListPlansAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    Task<bool> PlanNameExistsAsync(
        string name, Guid? excludePlanId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetPlanNamesAsync(
        IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken = default);

    void Add(MembershipPlan plan);

    // Membresías
    Task<Membership?> GetByIdAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Membership>> GetHistoryAsync(
        Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// La membresía Active o congelada del miembro con el <c>end_date</c> más lejano que aún no venció
    /// (para encadenar la renovación). Una cancelada no cuenta: su hueco queda libre.
    /// </summary>
    /// <summary>Membresías Active del miembro que empiezan después de <paramref name="date"/> (las encadenadas detrás).</summary>
    Task<IReadOnlyList<Membership>> GetActiveStartingAfterAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

    Task<Membership?> GetLatestOpenAsync(
        Guid memberId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Membresías Active del gym con <c>end_date</c> entre hoy y hoy+<paramref name="withinDays"/> (inclusive).</summary>
    Task<IReadOnlyList<ExpiringMembershipRow>> GetExpiringAsync(
        Guid gymId, DateOnly today, int withinDays, CancellationToken cancellationToken = default);

    void Add(Membership membership);

    // Congelamientos (MS4)
    /// <summary>Congelamientos de esas membresías (historial completo), del más antiguo al más reciente.</summary>
    Task<IReadOnlyList<MembershipFreeze>> GetFreezesAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default);

    Task<MembershipFreeze?> GetFreezeByIdAsync(Guid freezeId, CancellationToken cancellationToken = default);

    void Add(MembershipFreeze freeze);

    void Remove(MembershipFreeze freeze);

    // Tiqueteras (MS5)
    /// <summary>El consumo de la entrada de asistencia, si ya lo hay (consumir es idempotente por entrada).</summary>
    Task<PassUsage?> GetPassUsageByAttendanceAsync(Guid attendanceId, CancellationToken cancellationToken = default);

    /// <summary>Entradas consumidas de cada tiquetera pedida (las que no tienen consumo no aparecen).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPassUsedCountsAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default);

    /// <summary>Las entradas consumidas de esas tiqueteras, con su día, de la más antigua a la más reciente (MS8: historial del miembro).</summary>
    Task<IReadOnlyList<PassUsage>> GetPassUsagesAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default);

    /// <summary>Tiqueteras Active del miembro cuya vigencia cubre <paramref name="date"/> (con o sin entradas).</summary>
    Task<IReadOnlyList<Membership>> GetPassesCoveringAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>¿Hay una membresía por tiempo, Active y no congelada, que cubre ese día?</summary>
    Task<bool> HasTimeMembershipCoveringAsync(
        Guid memberId, DateOnly date, CancellationToken cancellationToken = default);

    void Add(PassUsage usage);

    void Remove(PassUsage usage);

    // Planes de grupo (MS7)
    Task<MembershipGroup?> GetGroupByIdAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>Todas las membresías del grupo (también las canceladas), el titular primero.</summary>
    Task<IReadOnlyList<Membership>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membresías por tiempo, Active, del miembro que cubren algún día de <paramref name="start"/>..<paramref name="end"/>
    /// (para no solapar al meter a alguien en un grupo).
    /// </summary>
    Task<IReadOnlyList<Membership>> GetActiveOverlappingAsync(
        Guid memberId, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    void Add(MembershipGroup group);

    // Ajustes del gym (gracia y aviso de vencimiento)
    Task<MembershipSettings?> GetSettingsAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(MembershipSettings settings);

    /// <summary>
    /// M6-2: reemplaza por <paramref name="replacement"/> los motivos de cancelación de las membresías del miembro y los
    /// de sus congelamientos. Las fechas y los días acreditados no se tocan. Idempotente.
    /// </summary>
    Task ReplaceReasonsForMemberAsync(
        Guid gymId, Guid memberId, string replacement, CancellationToken cancellationToken = default);
}

public sealed record ExpiringMembershipRow(
    Guid MembershipId, Guid MemberId, Guid PlanId, string PlanName, DateOnly EndDate);
