using GSM.Application.Common.Events;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Events;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Communications;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>
/// La tarea periódica de Comunicaciones (ver la especificación, §5). La invoca una tarea programada de
/// Dokploy por un endpoint interno. Genera los recordatorios de vencimiento <b>como eventos</b>
/// (<see cref="MembershipReminderDue"/>): Notifications crea los avisos y el dispatcher envía los correos.
/// <para>
/// Corre <b>sin usuario</b>, así que el Global Query Filter está desactivado y ve todos los gyms.
/// Es idempotente (Notifications deduplica) y tolera ejecuciones perdidas: si un día no corre, al
/// siguiente recupera los recordatorios que faltaban.
/// </para>
/// </summary>
public sealed class RunCommunicationsJobHandler(
    IMemberReader members,
    IMembershipReader memberships,
    ICommunicationsSettingsRepository settings,
    IDomainEventPublisher events,
    PublishBirthdayGreetingsHandler birthdays,
    ILogger<RunCommunicationsJobHandler> logger)
{
    /// <returns>Cuántos recordatorios de vencimiento y felicitaciones de cumpleaños se publicaron.</returns>
    public async Task<CommunicationsRunOutcome> HandleAsync(CancellationToken cancellationToken)
    {
        var reminders = await PublishRemindersAsync(cancellationToken);
        var greetings = await birthdays.HandleAsync(cancellationToken);
        logger.LogInformation(
            "Tarea de comunicaciones: {Reminders} recordatorios y {Birthdays} cumpleaños publicados", reminders, greetings);
        return new CommunicationsRunOutcome(reminders, greetings);
    }

    /// <summary>
    /// Qué recordatorio toca hoy (§H): de N días a 1 día antes, el día que vence, o el último día de la
    /// gracia. Este último no existe si el gym no da gracia (coincidiría con el día que vence).
    /// </summary>
    internal static MembershipReminderKind? KindFor(ReminderMembershipView candidate)
    {
        if (candidate.DaysLeft >= 1)
        {
            return MembershipReminderKind.DaysBefore;
        }

        if (candidate.DaysLeft == 0)
        {
            return MembershipReminderKind.ExpiresToday;
        }

        return candidate.GraceDays > 0 && candidate.DaysLeft == -candidate.GraceDays
            ? MembershipReminderKind.LastGraceDay
            : null;
    }

    private async Task<int> PublishRemindersAsync(CancellationToken cancellationToken)
    {
        var published = 0;

        // Gyms con miembros activos: los demás no tienen a quién avisar.
        var gymIds = (await members.GetActiveCountsByGymAsync(cancellationToken)).Keys;
        foreach (var gymId in gymIds)
        {
            var days = (await settings.GetAsync(gymId, cancellationToken))?.ReminderDaysBefore
                ?? CommunicationsSettings.DefaultReminderDaysBefore;
            if (days == 0)
            {
                continue;
            }

            foreach (var candidate in await memberships.GetReminderCandidatesAsync(gymId, days, cancellationToken))
            {
                var kind = KindFor(candidate);
                if (kind is null)
                {
                    continue;
                }

                // Quien ya renovó (tiene otra membresía que termina después) no necesita el aviso.
                var latestEnd = await memberships.GetLatestEndDateAsync(candidate.MemberId, cancellationToken);
                if (latestEnd is { } end && end > candidate.EndDate)
                {
                    continue;
                }

                await events.PublishAsync(
                    new MembershipReminderDue(
                        gymId, candidate.MemberId, candidate.MembershipId, candidate.PlanName,
                        candidate.EndDate, candidate.DaysLeft, kind.Value),
                    cancellationToken);
                published++;
            }
        }

        return published;
    }
}
