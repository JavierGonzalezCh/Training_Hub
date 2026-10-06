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
}

public sealed record ExpiringMembershipRow(
    Guid MembershipId, Guid MemberId, Guid PlanId, string PlanName, DateOnly EndDate);
