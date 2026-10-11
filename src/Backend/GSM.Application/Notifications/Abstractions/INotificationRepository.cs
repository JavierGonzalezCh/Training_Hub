using GSM.Application.Notifications.Public;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Abstractions;

public interface INotificationRepository
{
    void Add(Notification notification);

    void AddDelivery(NotificationDelivery delivery);

    Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sin seguimiento: para releer tras un <c>TryResolveAsync</c> (que escribe directo en la base).</summary>
    Task<Notification?> GetUntrackedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Aviso ya creado para esa clave de idempotencia (con seguimiento, para actualizarlo).</summary>
    Task<Notification?> FindAsync(
        NotificationType type, Guid? userId, Guid? memberId, Guid referenceId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        NotificationType type, Guid? userId, Guid? memberId, Guid referenceId,
        CancellationToken cancellationToken = default);

    /// <summary>De un tipo y una referencia, a qué miembros ya se les avisó.</summary>
    Task<IReadOnlySet<Guid>> GetNotifiedMemberIdsAsync(
        NotificationType type, Guid referenceId, CancellationToken cancellationToken = default);

    // ---- bandeja del miembro ----

    /// <summary>Del miembro, sin los anuncios (viven en el feed): no leídos primero, luego los más recientes.</summary>
    Task<(IReadOnlyList<Notification> Items, int Total)> ListForMemberAsync(
        Guid memberId, DateTimeOffset now, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountUnreadForMemberAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task MarkAllReadForMemberAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken = default);

    // ---- bandeja del staff ----

    Task<(IReadOnlyList<Notification> Items, int Total)> ListForStaffAsync(
        Guid userId, DateTimeOffset now, bool openOnly, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task<int> CountOpenForStaffAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Atiende el aviso de acción solo si seguía sin atender (el primero gana). <c>true</c> si lo atendió.</summary>
    Task<bool> TryResolveAsync(Guid id, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetOpenActionIdsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    // ---- dispatcher ----

    /// <summary>Pendientes cuya hora llegó, y retenidos (<c>held</c>) cuyo horario sin avisos ya terminó.</summary>
    Task<IReadOnlyList<Notification>> GetDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDelivery>> GetDeliveriesAsync(
        IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default);

    // ---- mantenimiento y lectura pública ----

    /// <summary>
    /// Borra los avisos creados antes de <paramref name="cutoff"/> (todos los gyms): los del miembro
    /// siempre, leídos o no; los del staff solo si están leídos o atendidos (una acción pendiente no se pierde).
    /// </summary>
    Task<int> PurgeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, AnnouncementEmailDelivery>> GetEmailDeliveryAsync(
        NotificationType type, IReadOnlyCollection<Guid> referenceIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Candado de Postgres (<c>pg_try_advisory_lock</c>) que garantiza que solo una copia de la API
/// procesa la cola. Implementado en Infrastructure.
/// </summary>
public interface INotificationDispatchLock
{
    /// <returns>El candado (se libera al desechar) o <c>null</c> si otra copia ya lo tiene.</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default);
}
