using GSM.Domain.Gyms;
using GSM.Domain.SuperAdmin;

namespace GSM.Application.SuperAdmin;

/// <summary>Cuentas de gyms de una foto mensual. Puro: sin base de datos ni reloj.</summary>
public sealed record GymCounts(
    decimal Mrr,
    int NewGyms,
    int TrialGymsAtClose,
    int ConvertedGyms,
    int LostGyms,
    int TrialsNotConverted,
    int ActiveGymsAtStart,
    int ActiveGymsAtClose,
    Dictionary<string, string> StatesAtClose);

public sealed record GymMetricInput(
    Guid GymId,
    GymStatus ManualStatus,
    decimal MonthlyPrice,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly PaidThrough,
    int TrialGraceDays);

public static class MonthlyMetricsCalculator
{
    /// <summary>
    /// Calcula las cuentas de gyms del mes que empieza en <paramref name="month"/>.
    /// </summary>
    /// <param name="previousStates">
    /// Estados al cierre del mes anterior (la foto previa). Si no hay foto, el estado inicial se deriva con la
    /// regla actual al último día del mes anterior; la suspensión manual no deja historial, así que en ese caso
    /// un gym suspendido se ve suspendido también al inicio (no cuenta como perdido).
    /// </param>
    public static GymCounts Compute(
        IReadOnlyList<GymMetricInput> gyms, DateOnly month, IReadOnlyDictionary<string, string>? previousStates)
    {
        var lastDay = month.AddMonths(1).AddDays(-1);
        var dayBefore = month.AddDays(-1);
        var states = new Dictionary<string, string>();
        decimal mrr = 0;
        int created = 0, trial = 0, converted = 0, lost = 0, trialsLost = 0, activeStart = 0, activeClose = 0;

        foreach (var gym in gyms)
        {
            // El gym todavía no existía al cierre: no entra en la foto.
            if (gym.StartedOn > lastDay)
            {
                continue;
            }

            var end = GymSubscription.DeriveStatus(gym.ManualStatus, lastDay, gym.PaidThrough, gym.TrialEndsOn, gym.TrialGraceDays);
            states[gym.GymId.ToString()] = end.ToString();

            var isNew = gym.StartedOn >= month;
            if (isNew)
            {
                created++;
            }

            EffectiveSubscriptionStatus? start = null;
            if (previousStates is not null)
            {
                if (previousStates.TryGetValue(gym.GymId.ToString(), out var s)
                    && Enum.TryParse<EffectiveSubscriptionStatus>(s, out var parsed))
                {
                    start = parsed;
                }
            }
            else if (!isNew)
            {
                start = GymSubscription.DeriveStatus(gym.ManualStatus, dayBefore, gym.PaidThrough, gym.TrialEndsOn, gym.TrialGraceDays);
            }

            var wasActive = start is EffectiveSubscriptionStatus.Active or EffectiveSubscriptionStatus.Overdue;
            if (wasActive)
            {
                activeStart++;
            }

            if (end is EffectiveSubscriptionStatus.Active or EffectiveSubscriptionStatus.Overdue)
            {
                activeClose++;
                mrr += gym.MonthlyPrice;
            }

            // Una prueba terminada sin pago, dentro de su gracia, sigue siendo una prueba sin convertir.
            if (end is EffectiveSubscriptionStatus.Trial or EffectiveSubscriptionStatus.TrialEnded)
            {
                trial++;
            }

            var endedLost = end is EffectiveSubscriptionStatus.Disabled or EffectiveSubscriptionStatus.Suspended;
            var wasInTrial = start is EffectiveSubscriptionStatus.Trial or EffectiveSubscriptionStatus.TrialEnded
                             || (start is null && gym.TrialEndsOn is not null);

            // P13: el churn solo cuenta clientes (al día o en mora al inicio); una prueba que se cae va aparte.
            if (wasActive && endedLost)
            {
                lost++;
            }
            else if (wasInTrial && endedLost)
            {
                trialsLost++;
            }
            if (wasInTrial && end == EffectiveSubscriptionStatus.Active)
            {
                converted++;
            }
        }

        return new GymCounts(mrr, created, trial, converted, lost, trialsLost, activeStart, activeClose, states);
    }
}
