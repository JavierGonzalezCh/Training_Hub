using GSM.Domain.Communications;

namespace GSM.Application.Communications.Abstractions;

public interface IMemberNotificationRepository
{
    Task<bool> ExistsAsync(Guid memberId, NotificationType type, Guid referenceId, CancellationToken cancellationToken = default);

    void Add(MemberNotification notification);

    Task<MemberNotification?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Del miembro: las no leídas primero, luego de la más reciente a la más antigua.</summary>
    Task<(IReadOnlyList<MemberNotification> Items, int Total)> ListForMemberAsync(
        Guid memberId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Borra las creadas antes de <paramref name="cutoff"/> (tarea periódica, todos los gyms).</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
