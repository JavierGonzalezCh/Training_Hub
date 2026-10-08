namespace GSM.Application.Notifications.Public;

/// <summary>Lectura que Notifications expone a otros módulos.</summary>
public interface INotificationReader
{
    /// <summary>Estado de los correos de cada anuncio (panel de anuncios de Comunicaciones).</summary>
    Task<IReadOnlyDictionary<Guid, AnnouncementEmailDelivery>> GetAnnouncementEmailDeliveryAsync(
        IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken = default);
}

public sealed record AnnouncementEmailDelivery(int Pending, int Sent, int Failed);
