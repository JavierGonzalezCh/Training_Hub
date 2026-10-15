using GSM.Domain.Classes;

namespace GSM.Application.Classes.Abstractions;

/// <summary>Acceso a <see cref="ClassSettings"/> (reglas de reserva, C3). Sin fila valen los valores por defecto. Sin <c>SaveChanges</c>.</summary>
public interface IClassSettingsRepository
{
    Task<ClassSettings?> GetAsync(Guid gymId, CancellationToken cancellationToken = default);

    void Add(ClassSettings settings);
}

/// <summary>Acceso a <see cref="BookingBlock"/> (penalización por no-show, C3). Global Query Filter por gym. Sin <c>SaveChanges</c>.</summary>
public interface IBookingBlockRepository
{
    Task<BookingBlock?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>El bloqueo vigente del miembro hoy (sin levantar y con <c>BlockedUntil &gt;= today</c>), si lo hay.</summary>
    Task<BookingBlock?> GetActiveAsync(Guid memberId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Instante en que se aplicó el último bloqueo del miembro (levantado o no), para volver a contar desde ahí.</summary>
    Task<DateTimeOffset?> GetLastAppliedAtAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BookingBlock>> ListActiveAsync(DateOnly today, CancellationToken cancellationToken = default);

    void Add(BookingBlock block);
}
