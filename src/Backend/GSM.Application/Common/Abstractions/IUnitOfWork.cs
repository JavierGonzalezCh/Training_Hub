namespace GSM.Application.Common.Abstractions;

/// <summary>
/// Persistencia transversal sobre el DbContext único. Los repositorios de módulo solo
/// agregan/consultan entidades; el caso de uso decide cuándo confirmar.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Ejecuta <paramref name="action"/> dentro de una transacción de base de datos.</summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
