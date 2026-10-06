using System.Globalization;
using GSM.Application.Common.Abstractions;
using GSM.Application.Communications.Abstractions;
using GSM.Application.Communications.Contracts;
using GSM.Application.Members.Public;
using GSM.Application.Memberships.Public;
using GSM.Domain.Communications;
using GSM.Domain.Members;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Communications;

/// <summary>
/// La primera tarea periódica del sistema (ver la especificación, §5). La invoca una tarea
/// programada de Dokploy por un endpoint interno; no hay scheduler dentro de la API.
/// <para>
/// Corre <b>sin usuario</b>, así que el Global Query Filter está desactivado y ve todos los gyms:
/// cada escritura fija su <c>gym_id</c> de forma explícita. Es idempotente y tolera ejecuciones
/// perdidas: si un día no corre, al siguiente recupera los recordatorios que faltaban.
/// </para>
/// </summary>
public sealed class RunCommunicationsJobHandler(
    IClock clock,
    IMemberReader members,
    IMembershipReader memberships,
    ICommunicationsSettingsRepository settings,
    IEmailOutboxRepository outbox,
    IMemberNotificationRepository notifications,
    IEmailSender emailSender,
    MemberNotifier notifier,
    IUnitOfWork unitOfWork,
    ILogger<RunCommunicationsJobHandler> logger)
{
    public const int DefaultBatchSize = 50;

    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task<CommunicationsJobResult> HandleAsync(int batchSize, CancellationToken cancellationToken)
    {
        var reminders = await CreateRemindersAsync(cancellationToken);
        var (sent, failed) = await DrainOutboxAsync(Math.Clamp(batchSize, 1, 500), cancellationToken);
        var purged = await notifications.PurgeOlderThanAsync(
            clock.UtcNow.AddDays(-MemberNotification.RetentionDays), cancellationToken);

        var result = new CommunicationsJobResult(reminders, sent, failed, purged);
        logger.LogInformation(
            "Tarea de comunicaciones: {Reminders} recordatorios, {Sent} correos enviados, {Failed} fallidos, {Purged} avisos purgados",
            reminders, sent, failed, purged);
        return result;
    }

    private async Task<int> CreateRemindersAsync(CancellationToken cancellationToken)
    {
        var created = 0;

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

                var contact = await members.GetContactAsync(expiring.MemberId, cancellationToken);
                if (contact is null || contact.Status != MemberStatus.Active)
                {
                    continue;
                }

                var when = expiring.DaysLeft switch
                {
                    0 => "hoy",
                    1 => "mañana",
                    _ => $"el {expiring.EndDate.ToString("dddd d 'de' MMMM", Colombia)}",
                };

                if (await notifier.NotifyAsync(
                        gymId, expiring.MemberId, NotificationType.MembershipExpiring, expiring.MembershipId,
                        $"Tu plan {expiring.PlanName} vence {when}",
                        "Pasa por recepción para renovarlo y seguir entrenando sin interrupciones.",
                        "/app", sendEmail: true, cancellationToken))
                {
                    created++;
                }
            }

            // Un guardado por gym: si uno falla, los anteriores ya quedaron.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    private async Task<(int Sent, int Failed)> DrainOutboxAsync(int batchSize, CancellationToken cancellationToken)
    {
        var sent = 0;
        var failed = 0;
        foreach (var item in await outbox.GetPendingBatchAsync(batchSize, cancellationToken))
        {
            try
            {
                await emailSender.SendAsync(
                    new EmailMessage(item.ToAddress, item.Subject, item.HtmlBody, item.TextBody), cancellationToken);
                item.MarkSent(clock.UtcNow);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.MarkAttemptFailed(ex.Message);
                if (item.Status == EmailStatus.Failed)
                {
                    failed++;
                }

                logger.LogWarning(ex, "Fallo al enviar el correo {EmailId} (intento {Attempt})", item.Id, item.Attempts);
            }

            // Se guarda tras cada envío: si la tarea se corta, no se reenvía lo ya enviado.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return (sent, failed);
    }
}
