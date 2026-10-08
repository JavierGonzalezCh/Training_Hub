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
}
