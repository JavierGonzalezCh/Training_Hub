using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Notifications;
using GSM.Domain.Communications;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>
/// Reacciona a un anuncio (§3.2 de la especificación): sin reacción previa la crea; con otra
/// distinta la cambia; con la misma la quita. <paramref name="kind"/> nulo = quitarla.
/// </summary>
public sealed class ReactToAnnouncementHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAnnouncementRepository announcements,
    IAnnouncementReactionRepository reactions,
    IUnitOfWork unitOfWork,
    ILogger<ReactToAnnouncementHandler> logger)
{
    internal static ReactionKind ParseKind(string kind)
        => Enum.TryParse<ReactionKind>(kind, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationDomainException("Reacción inválida: Like, Celebrate o Strong.");

    public async Task<ReactionResponse> HandleAsync(
        Guid announcementId, string? kind, CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await CommunicationsFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);

        var requested = kind is null ? (ReactionKind?)null : ParseKind(kind);

        var announcement = await announcements.GetAsync(announcementId, cancellationToken);
        if (announcement is null || !announcement.IsVisibleOn(clock.Today)
            || !await announcements.IsVisibleToMemberAsync(announcement, self.Id, cancellationToken))
        {
            throw new NotFoundException("Anuncio no encontrado.");
        }

        var existing = await reactions.GetAsync(announcementId, self.Id, cancellationToken);
        ReactionKind? result;
        if (existing is null)
        {
            if (requested is { } newKind)
            {
                reactions.Add(AnnouncementReaction.Create(gymId, announcementId, self.Id, newKind, clock.UtcNow));
            }

            result = requested;
        }
        else if (requested is null || requested == existing.Kind)
        {
            reactions.Remove(existing);
            result = null;
        }
        else
        {
            existing.Change(requested.Value, clock.UtcNow);
            result = requested;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Miembro {MemberId} reacciona al anuncio {AnnouncementId}: {Kind}",
            self.Id, announcementId, result?.ToString() ?? "(ninguna)");

        var counts = await reactions.CountAsync([announcementId], cancellationToken);
        return new ReactionResponse(
            announcementId, counts.GetValueOrDefault(announcementId) ?? ReactionCounts.None, result?.ToString());
    }
}

public sealed class MarkAnnouncementsSeenHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IMemberFeedStateRepository feedStates,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await CommunicationsFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);

        var state = await feedStates.GetAsync(self.Id, cancellationToken);
        if (state is null)
        {
            feedStates.Add(MemberFeedState.Create(gymId, self.Id, clock.UtcNow));
        }
        else
        {
            state.MarkSeen(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// No leídos para el indicador de la app. Sin estado previo, todos los anuncios vigentes cuentan
/// como no leídos: es la primera vez que el miembro entra.
/// </summary>
public sealed class GetUnreadCountHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    IAnnouncementRepository announcements,
    IMemberFeedStateRepository feedStates,
    CountMyUnreadNotificationsHandler unreadNotifications)
{
    public async Task<UnreadCountResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var gymId = currentUser.RequireGymId();
        await CommunicationsFeatureGate.EnsureMemberModuleEnabledAsync(gyms, gymId, cancellationToken);
        var self = await FeedMember.ResolveAsync(currentUser, members, cancellationToken);

        var state = await feedStates.GetAsync(self.Id, cancellationToken);
        var unread = await announcements.CountVisibleSinceAsync(self.Id, clock.Today, state?.AnnouncementsSeenAt, cancellationToken);

        var unreadCount = await unreadNotifications.HandleAsync(cancellationToken);
        return new UnreadCountResponse(unread, unreadCount);
    }
}
