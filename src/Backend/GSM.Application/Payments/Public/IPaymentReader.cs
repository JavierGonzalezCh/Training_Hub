using GSM.Domain.Payments;

namespace GSM.Application.Payments.Public;

/// <summary>Lectura que el módulo Payments expone a los demás módulos.</summary>
public interface IPaymentReader
{
    /// <summary>Suma de montos del gym en un mes (para el dashboard).</summary>
    Task<decimal> GetMonthlyRevenueAsync(
        Guid gymId, int year, int month, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberPaymentView>> GetRecentByMemberAsync(
        Guid memberId, int limit, CancellationToken cancellationToken = default);
}

public sealed record MemberPaymentView(
    Guid Id,
    decimal Amount,
    PaymentMethod Method,
    string Concept,
    DateTimeOffset PaidAt);
