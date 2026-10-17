using GSM.Application.Common.Events;

namespace GSM.Application.Communications.Events;

/// <summary>
/// Un anuncio programado no pudo salir a su hora (audiencia vacía o caducidad pasada) y volvió a borrador (CO4-1).
/// Notifications le avisa a su autor en la campana del staff. <paramref name="NoticeId"/> identifica este fallo:
/// si el anuncio se reprograma y vuelve a fallar, es otro aviso.
/// </summary>
public sealed record ScheduledAnnouncementNotPublished(
    Guid NoticeId,
    Guid AnnouncementId,
    Guid GymId,
    Guid AuthorUserId,
    string Title,
    string Reason) : IDomainEvent;
