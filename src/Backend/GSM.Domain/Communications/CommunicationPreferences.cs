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

    public const int BirthdayMessageMaxLength = 600;

    /// <summary>Texto de cumpleaños por defecto. Variables: <c>{nombre}</c> y <c>{gym}</c>.</summary>
    public const string DefaultBirthdayMessage =
        "¡Feliz cumpleaños, {nombre}! Hoy tu entrenamiento va por cuenta de la casa 🎉";

    /// <summary>¿Felicita el gym a sus miembros activos el día de su cumpleaños? Apagado por defecto (§F).</summary>
    public bool BirthdayEnabled { get; private set; }

    /// <summary>Texto propio del gym. Nulo = el texto por defecto.</summary>
    public string? BirthdayMessage { get; private set; }

    /// <summary>El texto vigente: el del gym o el de por defecto.</summary>
    public string EffectiveBirthdayMessage => BirthdayMessage ?? DefaultBirthdayMessage;

    public static CommunicationsSettings Default(Guid gymId) => new() { GymId = gymId };

    public void UpdateBirthday(bool enabled, string? message)
    {
        var trimmed = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (trimmed is not null && trimmed.Length > BirthdayMessageMaxLength)
        {
            throw new ValidationDomainException(
                $"El texto de cumpleaños admite hasta {BirthdayMessageMaxLength} caracteres.");
        }

        BirthdayEnabled = enabled;
        BirthdayMessage = trimmed;
    }

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
