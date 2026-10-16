using GSM.Domain.Payments;

namespace GSM.Application.Payments.Public;

/// <summary>Lectura que el módulo Payments expone a los demás módulos.</summary>
public interface IPaymentReader
{
    /// <summary>Suma de montos del gym en un mes (para el dashboard).</summary>
    Task<decimal> GetMonthlyRevenueAsync(
        Guid gymId, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suma de montos del gym entre dos días de negocio, ambos incluidos (B2: comparar el mes en curso contra los
    /// mismos días del mes anterior). Agrupa con <c>IClock.BusinessDayRange</c>, no con un rango UTC.
    /// </summary>
    Task<decimal> GetRevenueBetweenDaysAsync(
        Guid gymId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken = default);

    /// <summary>Suma de montos por gym en un mes de negocio, en una sola consulta (panel de SuperAdmin).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetMonthlyRevenueByGymAsync(
        int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lo que se pagó de verdad por una membresía: la suma de los asientos ligados a ella (cobros, y las anulaciones
    /// y devoluciones, que son negativos). Memberships lo usa para calcular el crédito de un cambio de plan.
    /// </summary>
    Task<decimal> GetNetPaidForMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberPaymentView>> GetRecentByMemberAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default);
}

public sealed record MemberPaymentView(
    Guid Id,
    decimal Amount,
    PaymentMethod Method,
    string Concept,
    DateTimeOffset PaidAt);
