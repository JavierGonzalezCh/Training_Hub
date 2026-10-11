using GSM.Application.Common.Events;
using GSM.Domain.Communications;

namespace GSM.Application.Communications.Events;

/// <summary>
/// Un anuncio pasó a publicado. Notifications lo consume y crea un aviso por miembro (y su correo si
/// <paramref name="SendEmail"/>). <paramref name="MemberIds"/> es la audiencia ya calculada por
/// Comunicaciones: los miembros activos del gym.
/// </summary>
public sealed record AnnouncementPublished(
    Guid AnnouncementId,
    Guid GymId,
    string Title,
    string Body,
    bool SendEmail,
    IReadOnlyList<Guid> MemberIds,
    AnnouncementCategory Category = AnnouncementCategory.GymNews) : IDomainEvent;
