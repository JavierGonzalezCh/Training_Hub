using GSM.Domain.Classes;

namespace GSM.Application.Classes.Abstractions;

/// <summary>
/// Acceso a <see cref="WaitlistEntry"/> (lista de espera, C4). Global Query Filter por gym; la tarea periódica
/// corre sin usuario y ve todos los gyms. Sin <c>SaveChanges</c>.
/// </summary>
public interface IWaitlistRepository
{
    Task<WaitlistEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>La entrada abierta (<c>waiting</c> u <c>offered</c>) del miembro en la sesión, si la hay.</summary>
    Task<WaitlistEntry?> GetOpenAsync(Guid sessionId, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Entradas abiertas de la sesión, por orden de posición.</summary>
    Task<IReadOnlyList<WaitlistEntry>> ListOpenForSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Entradas abiertas de varias sesiones (para calcular posiciones en el horario del miembro).</summary>
    Task<IReadOnlyList<WaitlistEntry>> ListOpenForSessionsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken = default);

    /// <summary>Entradas abiertas del miembro con su sesión y clase, para el horario y el conteo del máximo.</summary>
    Task<IReadOnlyList<(WaitlistEntry Entry, ClassSession Session, GymClass Class)>> ListOpenForMemberAsync(
        Guid memberId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Mayor posición dada en la sesión (en cualquier estado), o 0 si nunca hubo lista.</summary>
    Task<int> GetMaxPositionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Cupos apartados: ofertas vigentes (<c>offered</c>) de la sesión.</summary>
    Task<int> CountOffersAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entradas abiertas del miembro en sesiones que aún no empiezan (fecha posterior a <paramref name="today"/>, o de
    /// hoy con inicio posterior a <paramref name="nowTime"/>). Cuentan para el máximo de reservas activas.
    /// </summary>
    Task<int> CountFutureOpenForMemberAsync(
        Guid memberId, DateOnly today, TimeOnly nowTime, CancellationToken cancellationToken = default);

    /// <summary>Sesiones (de todos los gyms si no hay usuario) que tienen alguna entrada abierta: lo que revisa la tarea periódica.</summary>
    Task<IReadOnlyList<Guid>> ListSessionIdsWithOpenEntriesAsync(CancellationToken cancellationToken = default);

    void Add(WaitlistEntry entry);
}
