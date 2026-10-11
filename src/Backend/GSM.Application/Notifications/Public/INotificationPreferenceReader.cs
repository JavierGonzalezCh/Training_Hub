using GSM.Domain.Notifications;

namespace GSM.Application.Notifications.Public;

/// <summary>Lectura de las preferencias de los miembros que Notifications expone a otros módulos.</summary>
public interface INotificationPreferenceReader
{
    /// <summary>De <paramref name="memberIds"/>, los que reciben por correo los avisos de <paramref name="category"/>.</summary>
    Task<IReadOnlySet<Guid>> GetEmailEnabledAsync(
        IReadOnlyCollection<Guid> memberIds, NotificationCategory category, CancellationToken cancellationToken = default);
}
