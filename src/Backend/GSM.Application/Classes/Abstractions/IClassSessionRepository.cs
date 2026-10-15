using GSM.Domain.Classes;

namespace GSM.Application.Classes.Abstractions;

/// <summary>Acceso a <see cref="ClassSession"/>. El Global Query Filter restringe al gym actual. Sin <c>SaveChanges</c>.</summary>
public interface IClassSessionRepository
{
    /// <summary>Sesión materializada de esa ocurrencia, o <c>null</c> si nadie la ha tocado todavía.</summary>
    Task<ClassSession?> GetAsync(Guid classId, DateOnly sessionDate, CancellationToken cancellationToken = default);

    /// <summary>Sesiones materializadas de un rango de fechas (para proyectar el horario).</summary>
    Task<IReadOnlyList<ClassSession>> ListInRangeAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>C2: sesiones materializadas de una clase desde <paramref name="from"/> (inclusive) en adelante.</summary>
    Task<IReadOnlyList<ClassSession>> ListFromAsync(
        Guid classId, DateOnly from, CancellationToken cancellationToken = default);

    /// <summary>C4: sesión por id (la tarea periódica llega a ella desde la lista de espera).</summary>
    Task<ClassSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// C4: serializa los cambios de cupo de una sesión (reservar, confirmar una oferta, ofrecer). Toma un candado de
    /// transacción de Postgres: debe llamarse dentro de <c>IUnitOfWork.ExecuteInTransactionAsync</c>.
    /// </summary>
    Task LockAsync(Guid sessionId, CancellationToken cancellationToken = default);

    void Add(ClassSession session);
}
