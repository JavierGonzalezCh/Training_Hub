using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Communications;

/// <summary>
/// Reacción de un miembro a un anuncio: una por miembro y anuncio (índice único). Reaccionar con
/// otra la cambia; quitarla borra la fila.
/// </summary>
public sealed class AnnouncementReaction : BaseEntity, ITenantEntity
{
    private AnnouncementReaction()
    {
    }

    public Guid GymId { get; private set; }

    public Guid AnnouncementId { get; private set; }

    public Guid MemberId { get; private set; }

    public ReactionKind Kind { get; private set; }

    public DateTimeOffset ReactedAt { get; private set; }

    public static AnnouncementReaction Create(
        Guid gymId, Guid announcementId, Guid memberId, ReactionKind kind, DateTimeOffset now)
    {
        if (gymId == Guid.Empty || announcementId == Guid.Empty || memberId == Guid.Empty)
        {
            throw new ValidationDomainException("La reacción necesita gym, anuncio y miembro.");
        }

        return new AnnouncementReaction
        {
            GymId = gymId,
            AnnouncementId = announcementId,
            MemberId = memberId,
            Kind = kind,
            ReactedAt = now,
        };
    }

    public void Change(ReactionKind kind, DateTimeOffset now)
    {
        Kind = kind;
        ReactedAt = now;
    }
}
