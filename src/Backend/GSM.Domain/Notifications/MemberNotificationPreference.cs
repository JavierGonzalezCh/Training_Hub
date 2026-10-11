using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Notifications;

/// <summary>
/// Preferencia del miembro para una categoría de avisos, por canal (push y correo). Perezosa: sin fila
/// valen los valores por defecto (<see cref="Defaults"/>: todo encendido salvo Promociones). La bandeja
/// de la app no tiene preferencia: siempre recibe todo. Los avisos de seguridad y de cuenta tampoco.
/// </summary>
public sealed class MemberNotificationPreference : BaseEntity, ITenantEntity
{
    /// <summary>Categorías que el miembro puede configurar, en el orden en que se muestran.</summary>
    public static readonly IReadOnlyList<NotificationCategory> Configurable =
    [
        NotificationCategory.Membership,
        NotificationCategory.Classes,
        NotificationCategory.Progress,
        NotificationCategory.News,
        NotificationCategory.Promotions,
    ];

    private MemberNotificationPreference()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public NotificationCategory Category { get; private set; }

    public bool Push { get; private set; }

    public bool Email { get; private set; }

    /// <summary>Valor por defecto de una categoría: encendido, salvo Promociones (requiere aceptación).</summary>
    public static (bool Push, bool Email) Defaults(NotificationCategory category)
        => category == NotificationCategory.Promotions ? (false, false) : (true, true);

    public static bool IsConfigurable(NotificationCategory category) => Configurable.Contains(category);

    public static MemberNotificationPreference Create(
        Guid gymId, Guid memberId, NotificationCategory category, bool push, bool email)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("La preferencia necesita gym y miembro.");
        }

        if (!IsConfigurable(category))
        {
            throw new ValidationDomainException("Esa categoría de avisos no tiene preferencias.");
        }

        return new MemberNotificationPreference
        {
            GymId = gymId,
            MemberId = memberId,
            Category = category,
            Push = push,
            Email = email,
        };
    }

    public void Set(bool push, bool email)
    {
        Push = push;
        Email = email;
    }
}
