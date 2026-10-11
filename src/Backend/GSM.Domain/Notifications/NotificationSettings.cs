using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Notifications;

/// <summary>
/// Ajustes de avisos del gym: el horario sin avisos (21:00 a 7:00 por defecto). Perezosos: sin fila valen
/// los valores por defecto y la fila se crea en el primer cambio. Las horas son de pared en la zona del gym.
/// </summary>
public sealed class NotificationSettings : BaseEntity, ITenantEntity
{
    public static readonly TimeOnly DefaultQuietStart = new(21, 0);
    public static readonly TimeOnly DefaultQuietEnd = new(7, 0);

    private NotificationSettings()
    {
    }

    public Guid GymId { get; private set; }

    public bool QuietHoursEnabled { get; private set; } = true;

    /// <summary>Hora local en que empieza el horario sin avisos.</summary>
    public TimeOnly QuietStart { get; private set; } = DefaultQuietStart;

    /// <summary>Hora local en que termina. Puede ser menor que el inicio (cruza la medianoche).</summary>
    public TimeOnly QuietEnd { get; private set; } = DefaultQuietEnd;

    public static NotificationSettings Default(Guid gymId) => new() { GymId = gymId };

    public void Update(bool enabled, TimeOnly start, TimeOnly end)
    {
        if (enabled && start == end)
        {
            throw new ValidationDomainException("El horario sin avisos debe empezar y terminar a horas distintas.");
        }

        QuietHoursEnabled = enabled;
        QuietStart = start;
        QuietEnd = end;
    }

    /// <summary>¿La hora local <paramref name="time"/> cae dentro del horario sin avisos?</summary>
    public bool IsQuiet(TimeOnly time)
    {
        if (!QuietHoursEnabled)
        {
            return false;
        }

        return QuietStart < QuietEnd
            ? time >= QuietStart && time < QuietEnd
            : time >= QuietStart || time < QuietEnd;
    }
}
