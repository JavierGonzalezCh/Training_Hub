using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Events;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>
/// Publica un anuncio: calcula la audiencia <b>en ese momento</b> (CO2), fija los destinatarios de un anuncio
/// segmentado en la misma transacción que el cambio de estado y publica <see cref="AnnouncementPublished"/>.
/// Lo comparten la publicación manual (<see cref="PublishAnnouncementHandler"/>) y la programada
/// (<see cref="PublishScheduledAnnouncementsHandler"/>, que corre sin usuario).
/// <para>
/// Todas las validaciones que pueden lanzar van <b>antes</b> de tocar el anuncio, así que si falla no queda
/// nada a medias en el seguimiento de cambios (la tarea programada necesita seguir con los demás).
/// </para>
/// <para>
/// Usa solo <c>announcement.GymId</c>: la audiencia sale de <c>GetActiveMemberIdsAsync(gymId)</c> y los demás
/// filtros (plan, clase, etiqueta, estado) se intersectan con ese conjunto, así que con el filtro global
/// apagado (sin usuario) un miembro de otro gym nunca entra (CO2-5).
/// </para>
/// </summary>
public sealed class AnnouncementPublisher(
    IClock clock,
    IAnnouncementRepository announcements,
    AnnouncementAudienceResolver audienceResolver,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<AnnouncementPublisher> logger)
{
    /// <returns>A cuántos miembros llegó.</returns>
    public async Task<int> PublishAsync(Announcement announcement, CancellationToken cancellationToken)
    {
        var audience = await audienceResolver.ResolveAsync(announcement.Audience, announcement.GymId, cancellationToken);
        if (announcement.IsSegmented && audience.Count == 0)
        {
            throw new ValidationDomainException("La audiencia elegida no tiene ningún miembro: ajusta los filtros.");
        }

        announcement.Publish(clock.UtcNow, clock.Today);

        if (announcement.IsSegmented)
        {
            announcements.AddRecipients(audience.Select(
                memberId => AnnouncementRecipient.Create(announcement.GymId, announcement.Id, memberId)));
            announcement.SetRecipientCount(audience.Count);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Notifications crea los avisos y los correos al consumir el evento. Si falla, el anuncio queda
        // publicado igual.
        await events.PublishAsync(
            new AnnouncementPublished(
                announcement.Id, announcement.GymId, announcement.Title, announcement.Body,
                announcement.SendEmail, audience, announcement.Category, announcement.IsImportant,
                announcement.ImageUrl),
            cancellationToken);

        logger.LogInformation(
            "Anuncio {AnnouncementId} publicado para {Audience} miembros", announcement.Id, audience.Count);
        return audience.Count;
    }
}
