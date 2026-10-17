using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin.Abstractions;

/// <summary>
/// Acceso de escritura/consulta del módulo SuperAdmin sobre las suscripciones. Sin <c>SaveChanges</c>.
/// Opera por encima del multi-tenancy: sus consultas recorren la base entera.
/// </summary>
public interface IGymSubscriptionRepository
{
    void Add(GymSubscription subscription);

    /// <summary>Suscripción de un gym, rastreada para escritura. <c>null</c> si no existe.</summary>
    Task<GymSubscription?> GetByGymIdAsync(Guid gymId, CancellationToken cancellationToken = default);

    /// <summary>Todas las suscripciones con el estado manual de su gym, proyección plana para la cartera.</summary>
    Task<IReadOnlyList<PortfolioRow>> GetPortfolioAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fila cruda de cartera: la suscripción más el dato mínimo del gym. El estado efectivo se calcula fuera.</summary>
public sealed record PortfolioRow(
    Guid GymId,
    string GymName,
    string GymSlug,
    GSM.Domain.Gyms.GymStatus ManualStatus,
    decimal MonthlyPrice,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly PaidThrough,
    Guid? SaasPlanId,
    int? MemberLimitOverride,
    int TrialGraceDays);
