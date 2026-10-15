using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Abstractions;

/// <summary>
/// Planes del SaaS y resolución del límite de miembros de un gym. Opera por encima del multi-tenancy
/// (no hay Global Query Filter sobre estas tablas). Sin <c>SaveChanges</c>.
/// </summary>
public interface ISaasPlanRepository
{
    void Add(SaasPlan plan);

    /// <summary>Plan rastreado para escritura. <c>null</c> si no existe.</summary>
    Task<SaasPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaasPlan>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Plan cuyo nombre coincide (sin distinguir mayúsculas), o <c>null</c>. Sirve al enum heredado.</summary>
    Task<SaasPlan?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Límite de miembros efectivo de un gym (override del cliente, o el de su plan), o <c>null</c> si el gym
    /// no tiene suscripción. Si la suscripción no apunta a un plan, se resuelve por el nombre del plan heredado.
    /// </summary>
    Task<GymMemberLimit?> GetGymLimitAsync(Guid gymId, CancellationToken cancellationToken = default);
}

/// <param name="PlanId">Plan del SaaS resuelto, o <c>null</c> si no existe fila para él.</param>
/// <param name="PlanLimit">Límite del plan (nulo = ilimitado).</param>
/// <param name="Override">Límite propio del cliente (nulo = sin override).</param>
public sealed record GymMemberLimit(Guid? PlanId, string PlanName, int? PlanLimit, int? Override)
{
    /// <summary>El que manda: el override si existe, si no el del plan. Nulo = sin límite.</summary>
    public int? Effective => Override ?? PlanLimit;
}
