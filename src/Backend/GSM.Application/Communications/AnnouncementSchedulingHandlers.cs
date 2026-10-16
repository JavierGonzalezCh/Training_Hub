using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>CO4: programa o reprograma la publicación de un borrador (o de un anuncio ya programado).</summary>
public sealed class ScheduleAnnouncementHandler(
    IClock clock,
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ILogger<ScheduleAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(
        Guid id, ScheduleAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        var day = DateOnly.FromDateTime(clock.ToBusinessLocal(request.ScheduledFor));
        announcement.Schedule(request.ScheduledFor, clock.UtcNow, day);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Anuncio {AnnouncementId} programado para {ScheduledFor:o}", id, request.ScheduledFor);
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

/// <summary>CO4: cancela la programación; el anuncio vuelve a borrador.</summary>
public sealed class UnscheduleAnnouncementHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ILogger<UnscheduleAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        announcement.Unschedule();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Anuncio {AnnouncementId}: programación cancelada", id);
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

/// <summary>CO4: sube o reemplaza la imagen del anuncio (JPG, PNG o WebP, hasta 5 MB) y borra la anterior.</summary>
public sealed class UploadAnnouncementImageHandler(
    ICurrentUserService currentUser,
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, FileToStore file, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        ImageUploadRules.EnsureValid(file);

        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        var previous = announcement.ImageUrl;
        var url = await storage.SaveAsync(file, "announcements", cancellationToken);
        announcement.SetImage(url);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

public sealed class RemoveAnnouncementImageHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IFileStorage storage,
    IUnitOfWork unitOfWork)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        var previous = announcement.ImageUrl;
        announcement.RemoveImage();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(previous))
        {
            await storage.DeleteAsync(previous, cancellationToken);
        }

        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

/// <summary>
/// CO4: la parte de la tarea periódica que publica los anuncios programados cuya hora ya llegó. Corre
/// <b>sin usuario</b> y ve todos los gyms: cada anuncio se publica con su propio <c>GymId</c>. Puede salir unos
/// minutos después de la hora elegida, nunca antes. Idempotente: solo toma anuncios en <c>Scheduled</c> y al
/// publicarlos dejan de serlo, así que una segunda ejecución no hace nada.
/// <para>
/// Si al llegar la hora ya no se puede publicar (audiencia vacía o caducidad pasada) vuelve a borrador y queda
/// un aviso en el log, en vez de reintentarse en cada ejecución para siempre.
/// </para>
/// </summary>
public sealed class PublishScheduledAnnouncementsHandler(
    IClock clock,
    IAnnouncementRepository announcements,
    AnnouncementPublisher publisher,
    IUnitOfWork unitOfWork,
    ILogger<PublishScheduledAnnouncementsHandler> logger)
{
    public const int BatchSize = 100;

    /// <returns>Cuántos anuncios se publicaron.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var due = await announcements.ListDueScheduledAsync(clock.UtcNow, BatchSize, cancellationToken);
        var published = 0;
        foreach (var announcement in due)
        {
            try
            {
                await publisher.PublishAsync(announcement, cancellationToken);
                published++;
            }
            catch (DomainException ex)
            {
                logger.LogWarning(
                    ex, "Anuncio programado {AnnouncementId} del gym {GymId} no se pudo publicar; vuelve a borrador",
                    announcement.Id, announcement.GymId);
                announcement.Unschedule();
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        if (due.Count > 0)
        {
            logger.LogInformation("Tarea de anuncios programados: {Published} de {Due} publicados", published, due.Count);
        }

        return published;
    }
}
