using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>Nombres de las categorías tal como las ve el miembro.</summary>
public static class NotificationCategoryLabels
{
    public static string Es(NotificationCategory category) => category switch
    {
        NotificationCategory.Membership => "Mi membresía",
        NotificationCategory.Classes => "Mis clases",
        NotificationCategory.Progress => "Mi progreso",
        NotificationCategory.News => "Novedades del gym",
        NotificationCategory.Promotions => "Promociones",
        _ => "Avisos de seguridad",
    };
}
