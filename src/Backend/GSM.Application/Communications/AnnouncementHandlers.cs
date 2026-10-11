using GSM.Application.Common;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Communications.Events;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications.Public;
using GSM.Domain.Communications;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

public sealed class ListAnnouncementsHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    INotificationReader notificationReader)
{
    public async Task<PagedResult<AnnouncementResponse>> HandleAsync(
        ListAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        AnnouncementStatus? status = query.Status is null
            ? null
            : Enum.Parse<AnnouncementStatus>(query.Status, ignoreCase: true);

        var (items, total) = await announcements.ListAsync(status, query.Page, query.PageSize, cancellationToken);
        var ids = items.Select(a => a.Id).ToList();
        var counts = await reactions.CountAsync(ids, cancellationToken);
        var delivery = await AnnouncementMapper.GetDeliveryAsync(notificationReader, ids, cancellationToken);
        return new PagedResult<AnnouncementResponse>(
            items.Select(a => AnnouncementMapper.ToResponse(
                a, counts.GetValueOrDefault(a.Id), delivery.GetValueOrDefault(a.Id))).ToList(),
            query.Page, query.PageSize, total);
    }
}

public sealed class GetAnnouncementHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    INotificationReader notificationReader)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, notificationReader, cancellationToken);
    }
}

public sealed class CreateAnnouncementHandler(
    ICurrentUserService currentUser,
    IAnnouncementRepository announcements,
    IUnitOfWork unitOfWork,
    ILogger<CreateAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(
        CreateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        var authorId = currentUser.UserId
            ?? throw new UnauthorizedDomainException("Sin usuario en el contexto.");

        if (request.Important)
        {
            AnnouncementImportance.EnsureOwner(currentUser);
        }

        if (request.Pinned)
        {
            await AnnouncementPinning.EnsureRoomAsync(announcements, null, cancellationToken);
        }

        var announcement = Announcement.CreateDraft(
            gymId, authorId, request.Title, request.Body, request.Pinned, request.ExpiresOn, request.SendEmail,
            request.Category, request.Important);
        announcements.Add(announcement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Anuncio {AnnouncementId} creado como borrador en el gym {GymId}", announcement.Id, gymId);
        return AnnouncementMapper.ToResponse(announcement, ReactionCounts.None, null);
    }
}

public sealed class UpdateAnnouncementHandler(
    ICurrentUserService currentUser,
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ILogger<UpdateAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(
        Guid id, UpdateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        // Marcar o desmarcar "importante" es solo del Owner (y solo en borrador, lo exige el dominio).
        if (request.Important is { } important && important != announcement.IsImportant)
        {
            AnnouncementImportance.EnsureOwner(currentUser);
            announcement.SetImportant(important, currentUser.UserId ?? Guid.Empty);
        }

        announcement.Edit(request.Title, request.Body, request.ExpiresOn, request.SendEmail, request.Category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Anuncio {AnnouncementId} editado", id);
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

public sealed class PublishAnnouncementHandler(
    IClock clock,
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    INotificationReader notificationReader,
    IMemberReader members,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<PublishAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        announcement.Publish(clock.UtcNow, clock.Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // La audiencia (los miembros activos del gym) se calcula aquí; Notifications crea los avisos y los
        // correos al consumir el evento. Si falla, el anuncio queda publicado igual.
        var audience = await members.GetActiveMemberIdsAsync(announcement.GymId, cancellationToken);
        await events.PublishAsync(
            new AnnouncementPublished(
                announcement.Id, announcement.GymId, announcement.Title, announcement.Body,
                announcement.SendEmail, audience, announcement.Category, announcement.IsImportant),
            cancellationToken);

        logger.LogInformation(
            "Anuncio {AnnouncementId} publicado para {Audience} miembros", id, audience.Count);
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, notificationReader, cancellationToken);
    }
}

public sealed class ArchiveAnnouncementHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ILogger<ArchiveAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        announcement.Archive();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Anuncio {AnnouncementId} archivado", id);
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

public sealed class PinAnnouncementHandler(
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ILogger<PinAnnouncementHandler> logger)
{
    public async Task<AnnouncementResponse> HandleAsync(Guid id, bool pinned, CancellationToken cancellationToken)
    {
        var announcement = await announcements.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Anuncio no encontrado.");

        if (pinned && !announcement.Pinned)
        {
            await AnnouncementPinning.EnsureRoomAsync(announcements, announcement.Id, cancellationToken);
            announcement.Pin();
        }
        else if (!pinned)
        {
            announcement.Unpin();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Anuncio {AnnouncementId} {Action}", id, pinned ? "fijado" : "desfijado");
        return await AnnouncementMapper.ToResponseAsync(announcement, reactions, null, cancellationToken);
    }
}

public sealed class GetMemberFeedHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions)
{
    public async Task<PagedResult<FeedItem>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await CommunicationsFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var (items, total) = await announcements.ListFeedAsync(clock.Today, page, pageSize, cancellationToken);
        var ids = items.Select(a => a.Id).ToList();
        var counts = await reactions.CountAsync(ids, cancellationToken);
        var own = await reactions.GetOwnAsync(self.Id, ids, cancellationToken);

        return new PagedResult<FeedItem>(
            items.Select(a => new FeedItem(
                a.Id, a.Title, a.Body, a.PublishedAt!.Value, a.Pinned,
                counts.GetValueOrDefault(a.Id) ?? ReactionCounts.None,
                own.TryGetValue(a.Id, out var kind) ? kind.ToString() : null,
                a.IsImportant)).ToList(),
            page, pageSize, total);
    }
}

internal static class AnnouncementImportance
{
    /// <summary>Solo el Owner marca un anuncio como importante (un SuperAdmin impersonando opera como Owner).</summary>
    public static void EnsureOwner(ICurrentUserService currentUser)
    {
        if (currentUser.Role != GSM.Domain.Identity.UserRole.Owner && !currentUser.IsImpersonating)
        {
            throw new ForbiddenDomainException("Solo el Owner puede marcar un anuncio como importante.");
        }
    }
}

internal static class AnnouncementPinning
{
    public static async Task EnsureRoomAsync(
        IAnnouncementRepository announcements, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (await announcements.CountPinnedAsync(excludingId, cancellationToken) >= Announcement.MaxPinned)
        {
            throw new ConflictException(
                $"Ya hay {Announcement.MaxPinned} anuncios fijados. Desfija uno antes de fijar otro.");
        }
    }
}

/// <summary>
/// Con <c>show_community = false</c> (reinterpretado como "mostrar comunicaciones") los endpoints
/// del miembro responden 403; el staff puede preparar anuncios antes de encender el módulo.
/// </summary>
internal static class CommunicationsFeatureGate
{
    public static async Task EnsureMemberModuleEnabledAsync(
        IGymReader gyms, Guid gymId, CancellationToken cancellationToken)
    {
        var flags = await gyms.GetFeatureFlagsAsync(gymId, cancellationToken);
        if (flags is null || !flags.ShowCommunity)
        {
            throw new ForbiddenDomainException("Las comunicaciones no están habilitadas en este gimnasio.");
        }
    }
}

internal static class AnnouncementMapper
{
    public static AnnouncementResponse ToResponse(Announcement a, ReactionCounts? reactions, EmailDelivery? delivery) => new(
        a.Id, a.Title, a.Body, a.Status.ToString(), a.PublishedAt, a.Pinned, a.ExpiresOn, a.SendEmail, a.CreatedAt,
        reactions ?? ReactionCounts.None, delivery, a.Category.ToString(), a.IsImportant, a.ImportantMarkedBy);

    public static async Task<IReadOnlyDictionary<Guid, EmailDelivery>> GetDeliveryAsync(
        INotificationReader notificationReader, IReadOnlyCollection<Guid> announcementIds, CancellationToken cancellationToken)
    {
        var rows = await notificationReader.GetAnnouncementEmailDeliveryAsync(announcementIds, cancellationToken);
        return rows.ToDictionary(r => r.Key, r => new EmailDelivery(r.Value.Pending, r.Value.Sent, r.Value.Failed));
    }

    public static async Task<AnnouncementResponse> ToResponseAsync(
        Announcement a,
        IAnnouncementReactionRepository reactions,
        INotificationReader? notificationReader,
        CancellationToken cancellationToken)
    {
        var counts = await reactions.CountAsync([a.Id], cancellationToken);
        var delivery = notificationReader is null
            ? null
            : (await GetDeliveryAsync(notificationReader, [a.Id], cancellationToken)).GetValueOrDefault(a.Id);
        return ToResponse(a, counts.GetValueOrDefault(a.Id), delivery);
    }
}

/// <summary>El miembro detrás del token. Sin ficha vinculada → 403, como en clases y rutinas.</summary>
internal static class FeedMember
{
    public static async Task<MemberInfo> ResolveAsync(
        ICurrentUserService currentUser, IMemberReader members, CancellationToken cancellationToken)
        => await members.ResolveByUserIdAsync(currentUser.UserId ?? Guid.Empty, cancellationToken)
            ?? throw new ForbiddenDomainException("Tu cuenta no está vinculada a un miembro.");
}
