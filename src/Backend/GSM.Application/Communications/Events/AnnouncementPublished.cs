using GSM.Application.Common.Events;

namespace GSM.Application.Communications.Events;

/// <summary>
/// Un anuncio pasó a publicado. Sin consumidor todavía: el encolado de emails llega en 13.4 y
/// queda reservado como disparador de push.
/// </summary>
public sealed record AnnouncementPublished(Guid AnnouncementId, Guid GymId, bool SendEmail) : IDomainEvent;
