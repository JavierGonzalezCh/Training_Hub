namespace GSM.Domain.Notifications;

/// <summary>
/// Tipo de aviso. Label en Postgres: <c>notifications.notification_type</c> (snake_case). Cada sub-etapa
/// que suma un aviso agrega su valor aquí.
/// </summary>
public enum NotificationType
{
    ClassCancelled,
    MembershipExpiring,
    AchievementUnlocked,
    Announcement,
    CheckinRejected,
    VoidRequested,

    /// <summary>
    /// Anuncio importante (N2): se salta el horario sin avisos y las preferencias de Novedades/Promociones, y
    /// no entra en la bandeja (vive fijo en el feed). Solo lo marca el Owner.
    /// </summary>
    ImportantAnnouncement,

    /// <summary>A quien pidió anular un pago: su solicitud fue rechazada, con la nota si la hay (P2).</summary>
    VoidRequestRejected,

    /// <summary>
    /// N5: recordatorio de la clase reservada, 10 minutos antes de empezar. Programado al confirmar la reserva
    /// (<c>ScheduledFor</c> en el futuro) y cancelable. Referencia: la ocurrencia (clase + día).
    /// </summary>
    ClassReminder,

    /// <summary>N5: recordatorio al instructor de la sesión que va a dar, 10 minutos antes. Informativo.</summary>
    InstructorClassReminder,

    /// <summary>N5: felicitación de cumpleaños, programada para las 8:00 por Comunicaciones (CO5).</summary>
    Birthday,

    /// <summary>
    /// C4: se liberó un cupo y es para ti; confirma antes de que venza la oferta. Urgente (sale siempre, sin esperar
    /// el horario sin avisos). Referencia: la entrada de la lista de espera.
    /// </summary>
    WaitlistOffer,

    /// <summary>C4: no confirmaste a tiempo; el cupo pasó al siguiente. Referencia: la entrada de la lista de espera.</summary>
    WaitlistOfferExpired,
}

/// <summary>Categoría para las preferencias del miembro (N4). Nula en los avisos del staff.</summary>
public enum NotificationCategory
{
    Membership,
    Classes,
    Progress,
    News,
    Promotions,
    Security,
}

public enum NotificationPriority
{
    Normal,
    Urgent,
}

/// <summary>
/// Estado del aviso. <c>Held</c> y <c>Skipped</c> existen para N4 (horario sin avisos y preferencias):
/// en N1 nada los asigna.
/// </summary>
public enum NotificationStatus
{
    Pending,
    Held,
    Sent,
    Failed,
    Cancelled,
    Skipped,
}

/// <summary>Canal de una entrega. <c>Push</c> existe para N2/N3; en N1 solo se usa <c>Email</c>.</summary>
public enum DeliveryChannel
{
    Push,
    Email,
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,

    /// <summary>El miembro apagó ese canal para la categoría del aviso (N4): no se envía.</summary>
    Skipped,
}
