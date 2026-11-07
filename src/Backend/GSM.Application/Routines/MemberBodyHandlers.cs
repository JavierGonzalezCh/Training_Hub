using GSM.Application.Common.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Application.Members.Public;
using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;
using Microsoft.Extensions.Logging;

namespace GSM.Application.Routines;

/// <summary>
/// Un músculo con su recuperación estimada (0–100). <c>Muscle</c> es el nombre del enum (<c>Chest</c>, <c>Lower_back</c>…).
/// <c>LastTrainedOn</c>: el último día con series que lo trabajaron (como principal o secundario); nulo si no hay.
/// </summary>
public sealed record MuscleRecoveryItem(string Muscle, int RecoveryPercent, DateOnly? LastTrainedOn);

/// <summary>
/// «Tu cuerpo hoy»: días desde el último entreno registrado (nulo si nunca registró), cuántos músculos están listos
/// (100 %) y la recuperación de cada músculo. Es una estimación a partir de los registros, no una medición.
/// </summary>
public sealed record MyBodyStatusResponse(int? DaysSinceLastWorkout, int ReadyCount, IReadOnlyList<MuscleRecoveryItem> Muscles);

/// <summary>Una semana (lunes a domingo): kilos movidos (Σ peso × repeticiones), series y días entrenados.</summary>
public sealed record WeeklyVolumeItem(DateOnly WeekStart, decimal VolumeKg, int Sets, int Sessions);

/// <summary>Un registro reducido a lo que necesita el cálculo: el día, los músculos del ejercicio y sus series.</summary>
public sealed record BodyLog(DateOnly PerformedOn, Muscle Primary, IReadOnlyList<Muscle> Secondary, IReadOnlyList<RoutineSetEntry> Sets);

/// <summary>
/// Recuperación muscular y volumen semanal, calculados al leer (función pura, sin tabla). Regla de recuperación, en
/// días de negocio: un registro deja al músculo principal en 0 % y lo recupera en 2 días (3 si tuvo más de 6 series
/// efectivas); a los secundarios los deja en 50 % y los recupera en 1 día (2 si tuvo más de 6 series). Si varios
/// registros tocan el mismo músculo, manda el que lo deja más cansado. Los calentamientos no cuentan. «Cuerpo
/// completo» como principal cuenta como secundario de todos los músculos.
/// </summary>
public static class MemberBodyCalculator
{
    /// <summary>Los músculos que se muestran en el mapa (todos menos «cuerpo completo»).</summary>
    public static readonly IReadOnlyList<Muscle> MapMuscles =
        Enum.GetValues<Muscle>().Where(m => m != Muscle.Full_body).ToList();

    public const int ManySets = 6;

    public static MyBodyStatusResponse Status(IEnumerable<BodyLog> logs, DateOnly today)
    {
        var effective = logs
            .Where(l => l.PerformedOn <= today)
            .Select(l => (Log: l, Sets: EffectiveSets(l.Sets)))
            .Where(x => x.Sets > 0)
            .ToList();

        var muscles = new List<MuscleRecoveryItem>(MapMuscles.Count);
        foreach (var muscle in MapMuscles)
        {
            var percent = 100.0;
            DateOnly? last = null;
            foreach (var (log, sets) in effective)
            {
                var primary = log.Primary == muscle;
                var secondary = !primary && (log.Secondary.Contains(muscle) || log.Primary == Muscle.Full_body);
                if (!primary && !secondary)
                {
                    continue;
                }

                last = last is null || log.PerformedOn > last ? log.PerformedOn : last;
                var elapsed = today.DayNumber - log.PerformedOn.DayNumber;
                var start = primary ? 0.0 : 50.0;
                var days = primary ? (sets > ManySets ? 3 : 2) : (sets > ManySets ? 2 : 1);
                var value = Math.Min(100.0, start + (100.0 - start) * elapsed / days);
                percent = Math.Min(percent, value);
            }

            muscles.Add(new MuscleRecoveryItem(muscle.ToString(), (int)Math.Round(percent), last));
        }

        int? daysSince = effective.Count == 0 ? null : today.DayNumber - effective.Max(x => x.Log.PerformedOn).DayNumber;
        return new MyBodyStatusResponse(daysSince, muscles.Count(m => m.RecoveryPercent >= 100), muscles);
    }

    /// <summary>Las últimas <paramref name="weeks"/> semanas hasta la de hoy, de la más antigua a la actual (con ceros).</summary>
    public static IReadOnlyList<WeeklyVolumeItem> WeeklyVolume(IEnumerable<BodyLog> logs, DateOnly today, int weeks)
    {
        var currentWeek = GetMyRoutineWeekHandler.MondayOf(today);
        var first = currentWeek.AddDays(-7 * (weeks - 1));
        var byWeek = logs
            .Where(l => l.PerformedOn >= first && l.PerformedOn <= today)
            .GroupBy(l => GetMyRoutineWeekHandler.MondayOf(l.PerformedOn))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<WeeklyVolumeItem>(weeks);
        for (var week = first; week <= currentWeek; week = week.AddDays(7))
        {
            if (!byWeek.TryGetValue(week, out var items))
            {
                result.Add(new WeeklyVolumeItem(week, 0m, 0, 0));
                continue;
            }

            var working = items.SelectMany(l => l.Sets).Where(s => s.SetType != SetType.Warmup).ToList();
            var volume = working.Sum(s => (s.WeightKg ?? 0m) * (s.Reps ?? 0));
            var sets = working.Count(IsEffective);
            var sessions = items.Where(l => EffectiveSets(l.Sets) > 0).Select(l => l.PerformedOn).Distinct().Count();
            result.Add(new WeeklyVolumeItem(week, volume, sets, sessions));
        }

        return result;
    }

    private static bool IsEffective(RoutineSetEntry s)
        => s.SetType != SetType.Warmup && (s.Reps is > 0 || s.WeightKg is > 0 || s.DurationSeconds is > 0 || s.DistanceM is > 0);

    private static int EffectiveSets(IReadOnlyList<RoutineSetEntry> sets) => sets.Count(IsEffective);
}

/// <summary>Lee los registros del miembro con los músculos de cada ejercicio, para <see cref="MemberBodyCalculator"/>.</summary>
public sealed class MemberBodyService(IRoutineLogRepository logs, IExerciseRepository exercises)
{
    public async Task<IReadOnlyList<BodyLog>> LoadAsync(Guid memberId, DateOnly from, CancellationToken cancellationToken)
    {
        var rows = (await logs.GetProgressLogsAsync(memberId, null, cancellationToken))
            .Where(r => r.PerformedOn >= from)
            .ToList();
        var summaries = await exercises.GetVisibleSummariesAsync(rows.Select(r => r.ExerciseId).Distinct().ToList(), cancellationToken);
        return rows
            .Where(r => summaries.ContainsKey(r.ExerciseId))
            .Select(r =>
            {
                var exercise = summaries[r.ExerciseId];
                return new BodyLog(r.PerformedOn, exercise.PrimaryMuscle, exercise.SecondaryMuscles ?? [], r.Sets);
            })
            .ToList();
    }
}

/// <summary>«Tu cuerpo hoy» de la app del miembro: recuperación estimada por músculo.</summary>
public sealed class GetMyBodyStatusHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    MemberBodyService body,
    ILogger<GetMyBodyStatusHandler> logger)
{
    public async Task<MyBodyStatusResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        var today = clock.Today;
        // Lo que pasó hace más de 3 días ya no cansa; se lee un poco más atrás solo para «días desde el último entreno».
        var logs = await body.LoadAsync(memberId, today.AddDays(-60), cancellationToken);
        var status = MemberBodyCalculator.Status(logs, today);
        logger.LogInformation("Estado muscular consultado por el miembro {MemberId}", memberId);
        return status;
    }
}

/// <summary>Volumen de las últimas semanas (app del miembro, pestaña Progreso).</summary>
public sealed class GetMyWeeklyVolumeHandler(
    ICurrentUserService currentUser,
    IClock clock,
    IGymReader gyms,
    IMemberReader members,
    MemberBodyService body,
    ILogger<GetMyWeeklyVolumeHandler> logger)
{
    public const int DefaultWeeks = 8;
    public const int MaxWeeks = 26;

    public async Task<IReadOnlyList<WeeklyVolumeItem>> HandleAsync(int? weeks, CancellationToken cancellationToken)
    {
        var memberId = await RoutineMemberContext.ResolveAsync(currentUser, gyms, members, cancellationToken);
        var count = Math.Clamp(weeks ?? DefaultWeeks, 1, MaxWeeks);
        var today = clock.Today;
        var logs = await body.LoadAsync(memberId, GetMyRoutineWeekHandler.MondayOf(today).AddDays(-7 * (count - 1)), cancellationToken);
        logger.LogInformation("Volumen semanal consultado por el miembro {MemberId}", memberId);
        return MemberBodyCalculator.WeeklyVolume(logs, today, count);
    }
}
