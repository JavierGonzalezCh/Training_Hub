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
    ILogger<RunCommunicationsJobHandler> logger)
{
    /// <returns>Cuántos recordatorios de vencimiento se publicaron.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var reminders = await PublishRemindersAsync(cancellationToken);
        logger.LogInformation("Tarea de comunicaciones: {Reminders} recordatorios publicados", reminders);
        return reminders;
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

            foreach (var expiring in await memberships.GetExpiringAsync(gymId, days, cancellationToken))
            {
                // Quien ya renovó (tiene otra membresía que termina después) no necesita el aviso.
                var latestEnd = await memberships.GetLatestEndDateAsync(expiring.MemberId, cancellationToken);
                if (latestEnd is { } end && end > expiring.EndDate)
                {
                    continue;
                }

                await events.PublishAsync(
                    new MembershipReminderDue(
                        gymId, expiring.MemberId, expiring.MembershipId, expiring.PlanName,
                        expiring.EndDate, expiring.DaysLeft),
                    cancellationToken);
                published++;
            }
        }

        return published;
    }
}
