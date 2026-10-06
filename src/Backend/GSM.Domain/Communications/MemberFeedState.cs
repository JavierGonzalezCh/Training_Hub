using GSM.Domain.Common;

namespace GSM.Domain.Communications;

/// <summary>
/// Hasta cuándo vio el miembro los anuncios: los publicados después cuentan como no leídos.
/// Una fila por miembro, creada la primera vez que abre el feed.
/// </summary>
public sealed class MemberFeedState : BaseEntity, ITenantEntity
{
    private MemberFeedState()
    {
    }

    public Guid GymId { get; private set; }

    public Guid MemberId { get; private set; }

    public DateTimeOffset AnnouncementsSeenAt { get; private set; }

    public static MemberFeedState Create(Guid gymId, Guid memberId, DateTimeOffset seenAt)
        => new() { GymId = gymId, MemberId = memberId, AnnouncementsSeenAt = seenAt };

    public void MarkSeen(DateTimeOffset now) => AnnouncementsSeenAt = now;
}
