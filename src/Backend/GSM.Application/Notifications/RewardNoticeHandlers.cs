using System.Globalization;
using GSM.Application.Achievements.Events;
using GSM.Application.Achievements.Public;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Members.Public;
using GSM.Domain.Members;
using GSM.Domain.Notifications;

namespace GSM.Application.Notifications;

/// <summary>
/// L5: al ganar un logro con premio que vence, programa el aviso "tu premio vence el 15 oct" 3 días antes, a las
/// 8:00 (si ya falta menos, sale de inmediato). Idempotente por el desbloqueo. Sin tarea periódica: es un aviso
/// programado de N5. Un premio sin vencimiento no avisa nada.
/// </summary>
public sealed class AchievementUnlockedSchedulesRewardReminder(
    IAchievementReader achievements,
    IMemberReader members,
    ScheduledNoticeService scheduler,
    IClock clock,
    IUnitOfWork unitOfWork) : IDomainEventHandler<AchievementUnlockedEvent>
{
    /// <summary>Cuántos días antes del vencimiento sale el aviso (spec Achievements §4).</summary>
    public const int LeadDays = 3;

    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task HandleAsync(AchievementUnlockedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var reward = await achievements.GetRewardAsync(domainEvent.MemberAchievementId, cancellationToken);
        if (reward?.ExpiresAt is not { } expiresAt || expiresAt <= clock.UtcNow)
        {
            return;
        }

        var member = await members.GetAsync(domainEvent.MemberId, cancellationToken);
        if (member is null || member.Status != MemberStatus.Active)
        {
            return;
        }

        var expiryDay = DateOnly.FromDateTime(clock.ToBusinessLocal(expiresAt));
        var remindAt = scheduler.At(expiryDay.AddDays(-LeadDays), ScheduledNoticeService.DailySendTime);
        if (remindAt < clock.UtcNow)
        {
            remindAt = scheduler.NextDailySend();
        }

        var title = $"Tu premio «{Shorten(reward.RewardDescription, 60)}» vence el {expiryDay.ToString("d MMM", Colombia)}";
        await scheduler.ScheduleForMemberAsync(
            new MemberNotice(
                domainEvent.GymId, domainEvent.MemberId, NotificationType.RewardExpiring, NotificationCategory.Progress,
                domainEvent.MemberAchievementId, title, "Reclámalo en recepción.", "/app/logros", remindAt),
            sendEmail: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string Shorten(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>L5: el premio se entregó o el logro se retiró; el aviso de vencimiento que no haya salido ya no aplica.</summary>
public sealed class AchievementRewardClosedCancelsReminder(
    ScheduledNoticeService scheduler,
    IUnitOfWork unitOfWork) : IDomainEventHandler<AchievementRewardClosed>
{
    public async Task HandleAsync(AchievementRewardClosed domainEvent, CancellationToken cancellationToken = default)
    {
        if (await scheduler.CancelForMemberAsync(
                NotificationType.RewardExpiring, domainEvent.MemberId, domainEvent.MemberAchievementId, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
