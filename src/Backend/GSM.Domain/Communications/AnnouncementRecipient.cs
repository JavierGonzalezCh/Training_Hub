using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>
/// Destinatario fijado al publicar un anuncio con audiencia por segmentos (CO2). Que un miembro renueve
/// al día siguiente no lo quita; quien se inscribe después no aparece. Un anuncio para "todos" no
/// guarda filas: lo ve todo el gym.
/// </summary>
public sealed class AnnouncementRecipient : BaseEntity, ITenantEntity
{
    private AnnouncementRecipient()
    {
    }

    public Guid GymId { get; private set; }

    public Guid AnnouncementId { get; private set; }

    public Guid MemberId { get; private set; }

    public static AnnouncementRecipient Create(Guid gymId, Guid announcementId, Guid memberId)
    {
        if (gymId == Guid.Empty || announcementId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("Destinatario incompleto.");
        }

        return new AnnouncementRecipient { GymId = gymId, AnnouncementId = announcementId, MemberId = memberId };
    }
}
