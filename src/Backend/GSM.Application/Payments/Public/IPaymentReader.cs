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
    /// y devoluciones, que son negativos). Lo consume Memberships por su puerto <c>IMembershipPaidAmountProvider</c> (crédito del cambio de plan).
    /// </summary>
    Task<decimal> GetNetPaidForMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>Lo mismo que <see cref="GetNetPaidForMembershipAsync"/> para varias membresías (sin pagos = sin fila).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetNetPaidForMembershipsAsync(
        IReadOnlyCollection<Guid> membershipIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberPaymentView>> GetRecentByMemberAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default);

    /// <summary>La caja abierta del gym en contexto, con su efectivo esperado de ahora mismo (D1). <c>null</c> si no hay.</summary>
    Task<OpenCashShiftView?> GetOpenCashShiftAsync(CancellationToken cancellationToken = default);

    /// <summary>Solicitudes de anulación pendientes del gym en contexto (D1).</summary>
    Task<int> GetPendingVoidRequestCountAsync(CancellationToken cancellationToken = default);
}

/// <param name="IsStale">Abierta hace más de 16 horas: caja olvidada (Payments §4.1).</param>
public sealed record OpenCashShiftView(
    Guid ShiftId, Guid OpenedBy, string? OpenedByName, DateTimeOffset OpenedAt, decimal ExpectedCash, bool IsStale);

public sealed record MemberPaymentView(
    Guid Id,
    decimal Amount,
    PaymentMethod Method,
    string Concept,
    DateTimeOffset PaidAt);
