using GSM.Application.Common.Abstractions;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// Reglas del horario sin avisos (N4, spec Comunicaciones §G): qué avisos esperan y hasta cuándo.
/// </summary>
public static class QuietHours
{
    /// <summary>
    /// Aplica a los avisos a miembros de Novedades y Promociones (incluye cumpleaños y anuncios) y solo si no
    /// son urgentes. Lo demás tiene hora propia o es urgente (recordatorio de clase, lista de espera,
    /// seguridad) y sale siempre; los avisos al staff no tienen horario.
    /// </summary>
    public static bool Applies(Notification notification)
        => notification.RecipientMemberId is not null
            && notification.Priority != NotificationPriority.Urgent
            && (notification.Category == NotificationCategory.News
                || notification.Category == NotificationCategory.Promotions);

    /// <summary>¿Es hora de silencio ahora, en la hora local del gym?</summary>
    public static bool IsQuietNow(NotificationSettings settings, IClock clock, DateTimeOffset now)
        => settings.IsQuiet(TimeOnly.FromDateTime(clock.ToBusinessLocal(now)));

    /// <summary>El próximo instante (UTC) en que termina el horario sin avisos, a partir de <paramref name="now"/>.</summary>
    public static DateTimeOffset NextEnd(NotificationSettings settings, IClock clock, DateTimeOffset now)
    {
        var local = clock.ToBusinessLocal(now);
        var day = DateOnly.FromDateTime(local);
        if (TimeOnly.FromDateTime(local) >= settings.QuietEnd)
        {
            day = day.AddDays(1);
        }

        return clock.BusinessDayRange(day).Start + settings.QuietEnd.ToTimeSpan();
    }
}
