using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Contracts;
using GSM.Application.Achievements.Events;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Achievements;
using GSM.Domain.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Achievements;

/// <summary>
/// Recepción entrega el premio de un logro ganado (L5, spec §4). Guarda quién y cuándo. 409 si no hay premio,
/// ya se entregó, venció o el logro fue retirado. Publica <see cref="AchievementRewardClosed"/> después de guardar.
/// </summary>
public sealed class DeliverRewardHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAchievementRepository achievements,
    IUnitOfWork unitOfWork,
    IDomainEventPublisher events,
    ILogger<DeliverRewardHandler> logger)
{
    public async Task<RewardDeliveredResponse> HandleAsync(
        Guid achievementId, Guid memberId, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();
        var staffId = currentUser.UserId ?? throw new ForbiddenDomainException("Falta la identidad de quien entrega.");

        var achievement = await achievements.GetByIdAsync(achievementId, cancellationToken)
            ?? throw new NotFoundException("Logro no encontrado.");
        _ = await members.GetAsync(memberId, cancellationToken)
            ?? throw new NotFoundException("Miembro no encontrado.");
        var row = await achievements.GetUnlockAsync(memberId, achievementId, cancellationToken)
            ?? throw new NotFoundException("El miembro no tiene este logro.");

        var now = clock.UtcNow;
        row.DeliverReward(staffId, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Premio del logro {AchievementId} entregado al miembro {MemberId} por {StaffId}", achievementId, memberId, staffId);

        await events.PublishAsync(
            new AchievementRewardClosed(row.Id, memberId, row.GymId, achievementId), cancellationToken);

        return new RewardDeliveredResponse(
            achievementId, memberId, achievement.Name, row.RewardDescription ?? string.Empty, now);
    }
}

/// <summary>
/// El Owner ve los premios entregados, pendientes y vencidos de su gym para saber cuánto le cuestan (spec §4).
/// <paramref name="status"/>: "pending", "delivered" o "expired"; vacío trae todos.
/// </summary>
public sealed class ListRewardsHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IMemberReader members,
    IAchievementRepository achievements)
{
    private const int MaxRows = 200;

    public async Task<RewardsOverviewResponse> HandleAsync(string? status, CancellationToken cancellationToken)
    {
        _ = currentUser.RequireGymId();

        RewardStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RewardStatus>(status, ignoreCase: true, out var parsed) || parsed == RewardStatus.None)
            {
                throw new ValidationDomainException("El estado debe ser pending, delivered o expired.");
            }

            filter = parsed;
        }

        var now = clock.UtcNow;
        var overview = await achievements.GetRewardOverviewAsync(filter, now, MaxRows, cancellationToken);
        var names = await members.GetFullNamesAsync(
            overview.Rows.Select(r => r.Row.MemberId).Distinct().ToList(), cancellationToken);

        var items = overview.Rows
            .Select(r => new RewardListItem(
                r.Row.AchievementId,
                r.AchievementName,
                r.Row.MemberId,
                names.GetValueOrDefault(r.Row.MemberId, "Miembro"),
                r.Row.RewardDescription ?? string.Empty,
                r.Row.EffectiveRewardStatus(now).ToString(),
                r.Row.UnlockedAt,
                r.Row.RewardExpiresAt,
                r.Row.RewardDeliveredAt,
                r.Row.RewardDeliveredBy))
            .ToList();

        return new RewardsOverviewResponse(overview.Pending, overview.Delivered, overview.Expired, items);
    }
}
