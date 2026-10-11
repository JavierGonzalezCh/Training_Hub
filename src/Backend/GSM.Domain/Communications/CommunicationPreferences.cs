using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>
/// Ajustes de comunicaciones del gym. Perezosos, como los de check-in: sin fila se usan los
/// valores por defecto y la fila se crea en el primer cambio.
/// </summary>
public sealed class CommunicationsSettings : BaseEntity, ITenantEntity
{
    public const int DefaultReminderDaysBefore = 3;
    public const int MaxReminderDaysBefore = 14;

    private CommunicationsSettings()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Días antes del vencimiento en que se avisa. 0 = recordatorios desactivados.</summary>
    public int ReminderDaysBefore { get; private set; } = DefaultReminderDaysBefore;

    public static CommunicationsSettings Default(Guid gymId) => new() { GymId = gymId };

    public void Update(int reminderDaysBefore)
    {
        if (reminderDaysBefore is < 0 or > MaxReminderDaysBefore)
        {
            throw new ValidationDomainException(
                $"Los días de antelación deben estar entre 0 y {MaxReminderDaysBefore}.");
        }

        ReminderDaysBefore = reminderDaysBefore;
    }
}
