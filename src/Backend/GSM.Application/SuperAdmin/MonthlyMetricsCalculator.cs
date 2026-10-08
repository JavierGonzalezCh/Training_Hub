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
    int ActiveGymsAtStart,
    int ActiveGymsAtClose,
    Dictionary<string, string> StatesAtClose);

public sealed record GymMetricInput(
    Guid GymId,
    GymStatus ManualStatus,
    decimal MonthlyPrice,
    DateOnly StartedOn,
    DateOnly? TrialEndsOn,
    DateOnly PaidThrough);

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
        int created = 0, trial = 0, converted = 0, lost = 0, activeStart = 0, activeClose = 0;

        foreach (var gym in gyms)
        {
            // El gym todavía no existía al cierre: no entra en la foto.
            if (gym.StartedOn > lastDay)
            {
                continue;
            }

            var end = GymSubscription.DeriveStatus(gym.ManualStatus, lastDay, gym.PaidThrough, gym.TrialEndsOn);
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
                start = GymSubscription.DeriveStatus(gym.ManualStatus, dayBefore, gym.PaidThrough, gym.TrialEndsOn);
            }

            if (start is EffectiveSubscriptionStatus.Active or EffectiveSubscriptionStatus.Overdue)
            {
                activeStart++;
            }

            if (end is EffectiveSubscriptionStatus.Active or EffectiveSubscriptionStatus.Overdue)
            {
                activeClose++;
                mrr += gym.MonthlyPrice;
            }

            if (end == EffectiveSubscriptionStatus.Trial)
            {
                trial++;
            }

            var startedLost = start is EffectiveSubscriptionStatus.Disabled or EffectiveSubscriptionStatus.Suspended;
            var endedLost = end is EffectiveSubscriptionStatus.Disabled or EffectiveSubscriptionStatus.Suspended;
            if (!startedLost && endedLost)
            {
                lost++;
            }

            var wasInTrial = start == EffectiveSubscriptionStatus.Trial
                             || (start is null && gym.TrialEndsOn is not null);
            if (wasInTrial && end == EffectiveSubscriptionStatus.Active)
            {
                converted++;
            }
        }

        return new GymCounts(mrr, created, trial, converted, lost, activeStart, activeClose, states);
    }
}
