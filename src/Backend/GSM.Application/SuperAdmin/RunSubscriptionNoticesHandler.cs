using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Events;
using GSM.Application.Memberships.Public;
using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Events;
using GSM.Domain.SuperAdmin;
using Microsoft.Extensions.Logging;

namespace GSM.Application.SuperAdmin;

/// <summary>
/// S3: parte de la tarea periódica. Recorre las suscripciones y publica <see cref="SubscriptionNoticeDue"/> por cada
/// hito de calendario ya alcanzado (SuperAdmin §V3): prueba por terminar o terminada, vencimiento próximo o de hoy,
/// mora, inhabilitación próxima o consumada, y el límite de miembros (90 % y 100 %). No escribe nada propio: Notifications
/// crea los avisos, idempotentes por la clave de cada hito. Como la condición es "ya se alcanzó y la situación sigue
/// igual" y no "es exactamente hoy", una ejecución perdida se recupera en la siguiente. Corre sin usuario: ve todos los gyms.
/// <para>
/// La suspensión, la reactivación y el pago registrado no pasan por aquí: los publica el caso de uso que los provoca.
/// </para>
/// </summary>
public sealed class RunSubscriptionNoticesHandler(
    IClock clock,
    IGymSubscriptionRepository subscriptions,
    ISaasPlanRepository saasPlans,
    IMembershipReader memberships,
    IDomainEventPublisher events,
    ILogger<RunSubscriptionNoticesHandler> logger)
{
    /// <summary>Días antes del fin de la prueba en que se avisa.</summary>
    public const int TrialWarningDays = 3;

    /// <summary>Días antes del vencimiento en que se avisa.</summary>
    public const int ExpiryWarningDays = 5;

    /// <summary>Días antes de inhabilitarse en que se avisa.</summary>
    public const int DisableWarningDays = 3;

    /// <summary>Porcentaje del límite de miembros desde el que se avisa.</summary>
    public const int LimitWarningPercent = 90;

    /// <returns>Cuántos eventos publicó (no cuántos avisos nuevos: los repetidos los descarta Notifications).</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var rows = await subscriptions.GetPortfolioAsync(cancellationToken);
        var published = 0;

        foreach (var row in rows)
        {
            var due = new List<SubscriptionNoticeDue>();
            var status = GymSubscription.DeriveStatus(
                row.ManualStatus, today, row.PaidThrough, row.TrialEndsOn, row.TrialGraceDays);
            var daysLeft = GymSubscription.DaysUntilDisabled(
                row.ManualStatus, today, row.PaidThrough, row.TrialEndsOn, row.TrialGraceDays);

            switch (status)
            {
                case EffectiveSubscriptionStatus.Trial:
                    // Una prueba que ya pagó más allá de su fin no está "por terminar".
                    if (row.TrialEndsOn is { } endingOn && row.PaidThrough <= endingOn
                        && endingOn.DayNumber - today.DayNumber <= TrialWarningDays)
                    {
                        due.Add(Make(
                            row.GymId, SubscriptionNoticeKind.TrialEnding, endingOn, endingOn,
                            endingOn.DayNumber - today.DayNumber));
                    }

                    break;

                case EffectiveSubscriptionStatus.TrialEnded when row.TrialEndsOn is { } endedOn:
                {
                    var lastDay = endedOn.AddDays(row.TrialGraceDays);
                    due.Add(Make(row.GymId, SubscriptionNoticeKind.TrialEnded, endedOn, lastDay, daysLeft));
                    // La gracia por defecto (3 días) ya es el aviso de "terminó": el de "se inhabilitará pronto" solo
                    // aporta si la gracia de este cliente es más larga.
                    if (row.TrialGraceDays > DisableWarningDays && daysLeft <= DisableWarningDays)
                    {
                        due.Add(Make(row.GymId, SubscriptionNoticeKind.DisableSoon, endedOn, lastDay, daysLeft));
                    }

                    break;
                }

                case EffectiveSubscriptionStatus.Active:
                {
                    var diff = row.PaidThrough.DayNumber - today.DayNumber;
                    if (diff == 0)
                    {
                        due.Add(Make(row.GymId, SubscriptionNoticeKind.ExpiresToday, row.PaidThrough, row.PaidThrough, 0));
                    }
                    else if (diff <= ExpiryWarningDays)
                    {
                        due.Add(Make(row.GymId, SubscriptionNoticeKind.Expiring, row.PaidThrough, row.PaidThrough, diff));
                    }

                    break;
                }

                case EffectiveSubscriptionStatus.Overdue:
                {
                    var lastDay = row.PaidThrough.AddDays(GymSubscription.GraceDays);
                    due.Add(Make(row.GymId, SubscriptionNoticeKind.Overdue, row.PaidThrough, lastDay, daysLeft));
                    if (daysLeft <= DisableWarningDays)
                    {
                        due.Add(Make(row.GymId, SubscriptionNoticeKind.DisableSoon, row.PaidThrough, lastDay, daysLeft));
                    }

                    break;
                }

                case EffectiveSubscriptionStatus.Disabled:
                    due.Add(Make(row.GymId, SubscriptionNoticeKind.Disabled, row.PaidThrough, row.PaidThrough, null));
                    break;
            }

            // Suspendido o inhabilitado: no hay cupos que cuidar.
            if (status is not (EffectiveSubscriptionStatus.Suspended or EffectiveSubscriptionStatus.Disabled)
                && await TryLimitNoticeAsync(row.GymId, cancellationToken) is { } limitNotice)
            {
                due.Add(limitNotice);
            }

            foreach (var notice in due)
            {
                await events.PublishAsync(notice, cancellationToken);
                published++;
            }
        }

        logger.LogInformation(
            "Avisos de suscripción: {Count} eventos publicados para {Gyms} suscripciones", published, rows.Count);
        return published;
    }

    private static SubscriptionNoticeDue Make(Guid gymId, SubscriptionNoticeKind kind, DateOnly key, DateOnly date, int? days)
        => new(gymId, kind, SubscriptionNoticeReference.For(gymId, kind, key), date, days, null, null, null);

    /// <summary>
    /// Al 90 % del límite y al llegar a él (§V1). La clave incluye el límite: una clave por límite vigente, así que
    /// ampliar el plan y volver a llenarlo sí avisa de nuevo, y oscilar alrededor del 90 % no repite el aviso.
    /// </summary>
    private async Task<SubscriptionNoticeDue?> TryLimitNoticeAsync(Guid gymId, CancellationToken cancellationToken)
    {
        var limit = (await saasPlans.GetGymLimitAsync(gymId, cancellationToken))?.Effective;
        if (limit is not { } max)
        {
            return null;
        }

        var counted = await memberships.GetCountedMemberCountAsync(gymId, cancellationToken);
        SubscriptionNoticeKind? kind = counted >= max
            ? SubscriptionNoticeKind.MemberLimitReached
            : counted * 100 >= max * LimitWarningPercent
                ? SubscriptionNoticeKind.MemberLimitNear
                : null;
        if (kind is null)
        {
            return null;
        }

        return new SubscriptionNoticeDue(
            gymId, kind.Value, SubscriptionNoticeReference.For(gymId, kind.Value, $"limit-{max}"),
            null, null, null, counted, max);
    }
}
