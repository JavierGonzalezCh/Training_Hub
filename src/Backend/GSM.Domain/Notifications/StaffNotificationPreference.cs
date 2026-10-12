using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Notifications;

/// <summary>
/// Qué avisos quiere recibir en el teléfono (push) una persona del equipo, por tipo de aviso y gym
/// (spec Notifications §8). Perezosa: sin fila vale <see cref="DefaultPush"/>. El horario sin avisos del
/// gym no aplica al staff: cada persona apaga lo que no quiere.
/// </summary>
public sealed class StaffNotificationPreference : BaseEntity, ITenantEntity
{
    /// <summary>Tipos de aviso al staff que se pueden configurar, en el orden en que se muestran.</summary>
    public static readonly IReadOnlyList<NotificationType> Configurable = [NotificationType.CheckinRejected];

    private StaffNotificationPreference()
    {
    }

    public Guid GymId { get; private set; }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public bool Push { get; private set; }

    public static bool IsConfigurable(NotificationType type) => Configurable.Contains(type);

    /// <summary>
    /// Por defecto (§8): encendido, salvo el intento de entrada rechazado ("puede haber muchos al día").
    /// </summary>
    public static bool DefaultPush(NotificationType type) => type != NotificationType.CheckinRejected;

    public static StaffNotificationPreference Create(Guid gymId, Guid userId, NotificationType type, bool push)
    {
        if (gymId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ValidationDomainException("La preferencia necesita gym y persona.");
        }

        if (!IsConfigurable(type))
        {
            throw new ValidationDomainException("Ese aviso no tiene preferencias para el staff.");
        }

        return new StaffNotificationPreference { GymId = gymId, UserId = userId, Type = type, Push = push };
    }

    public void Set(bool push) => Push = push;
}
