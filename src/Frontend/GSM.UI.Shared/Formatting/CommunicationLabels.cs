using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>Textos e iconos del módulo Comunicaciones (etapa 13), compartidos por panel y app.</summary>
public static class CommunicationLabels
{
    public const string Like = "Like";
    public const string Celebrate = "Celebrate";
    public const string Strong = "Strong";

    public static IReadOnlyList<string> Reactions { get; } = [Like, Celebrate, Strong];

    public static string Status(string status) => status switch
    {
        "Draft" => "Borrador",
        "Scheduled" => "Programado",
        "Published" => "Publicado",
        "Archived" => "Archivado",
        _ => status,
    };

    /// <summary>Iconos de las reacciones: SVG de Material, nunca emojis como icono.</summary>
    public static string ReactionIcon(string kind) => kind switch
    {
        Celebrate => Icons.Material.Outlined.Celebration,
        Strong => Icons.Material.Outlined.FitnessCenter,
        _ => Icons.Material.Outlined.ThumbUp,
    };

    public static string ReactionLabel(string kind) => kind switch
    {
        Celebrate => "Celebrar",
        Strong => "Fuerza",
        _ => "Me gusta",
    };

    public static string NotificationIcon(string type) => type switch
    {
        "ClassCancelled" => Icons.Material.Outlined.EventBusy,
        "ClassReminder" => Icons.Material.Outlined.Alarm,
        "Birthday" => Icons.Material.Outlined.Cake,
        "MembershipExpiring" => Icons.Material.Outlined.HourglassBottom,
        "AchievementUnlocked" => Icons.Material.Outlined.EmojiEvents,
        _ => Icons.Material.Outlined.Notifications,
    };

    /// <summary>"hace 5 min", "hace 3 h", "ayer", o la fecha corta si es más antiguo.</summary>
    public static string Relative(DateTimeOffset instant, DateTimeOffset now)
    {
        var elapsed = now - instant;
        if (elapsed.TotalMinutes < 1)
        {
            return "ahora";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"hace {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"hace {(int)elapsed.TotalHours} h";
        }

        return elapsed.TotalDays < 2 ? "ayer" : GsmFormat.Instant(instant).Split(',')[0];
    }
}
