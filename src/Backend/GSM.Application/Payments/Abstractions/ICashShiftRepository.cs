using GSM.Domain.Payments;

namespace GSM.Application.Payments.Abstractions;

/// <summary>Acceso a los turnos de caja y sus movimientos (<c>payments.cash_shifts</c>, <c>cash_movements</c>). Sin <c>SaveChanges</c>.</summary>
public interface ICashShiftRepository
{
    void Add(CashShift shift);

    void Add(CashMovement movement);

    /// <summary>La caja abierta del gym actual (con seguimiento, para cerrarla), si la hay.</summary>
    Task<CashShift?> GetOpenAsync(CancellationToken cancellationToken = default);

    Task<CashShift?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>El último turno cerrado: su efectivo contado precarga la base del siguiente.</summary>
    Task<CashShift?> GetLastClosedAsync(CancellationToken cancellationToken = default);

    /// <summary>De <paramref name="shiftIds"/>, los turnos que siguen abiertos.</summary>
    Task<IReadOnlySet<Guid>> GetOpenShiftIdsAsync(
        IReadOnlyCollection<Guid> shiftIds, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CashShift> Items, int Total)> GetPageAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? openedBy, int page, int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Turnos cuya apertura cae en <c>[fromUtc, toUtc)</c>, del más antiguo al más reciente.</summary>
    Task<IReadOnlyList<CashShift>> GetOpenedInRangeAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CashMovement>> GetMovementsAsync(
        IReadOnlyCollection<Guid> shiftIds, CancellationToken cancellationToken = default);

    Task<(decimal Deposits, decimal Withdrawals)> GetMovementTotalsAsync(
        Guid shiftId, CancellationToken cancellationToken = default);
}
