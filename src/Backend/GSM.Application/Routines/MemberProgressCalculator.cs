using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>Un registro de rutina reducido a lo que necesita el progreso.</summary>
public sealed record ProgressLog(DateOnly PerformedOn, bool SelfAssigned, IReadOnlyList<RoutineSetEntry> Sets);

/// <summary>Claves de los récords personales (ver <see cref="PersonalRecordItem"/>).</summary>
public static class RecordKinds
{
    public const string BestWeight = "best_weight";
    public const string BestSetVolume = "best_set_volume";
    public const string BestSessionVolume = "best_session_volume";
    public const string BestReps = "best_reps";
    public const string BestSessionReps = "best_session_reps";
    public const string BestDuration = "best_duration";
    public const string BestDistance = "best_distance";
}

/// <summary>
/// Cálculo puro del progreso (R7). Sin acceso a datos: recibe los registros de un ejercicio y devuelve puntos y
/// récords. Los calentamientos (<see cref="SetType.Warmup"/>) no cuentan. Las repeticiones de un ejercicio unilateral
/// son por lado, tal como se registraron.
/// </summary>
public static class MemberProgressCalculator
{
    /// <summary>Un punto por día con al menos una serie con valores; junta las líneas del mismo día.</summary>
    public static IReadOnlyList<ProgressPoint> Points(IEnumerable<ProgressLog> logs)
        => logs
            .GroupBy(l => l.PerformedOn)
            .OrderBy(g => g.Key)
            .Select(g => PointOf(g.Key, g.SelectMany(l => l.Sets)))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    public static ProgressPoint? PointOf(DateOnly day, IEnumerable<RoutineSetEntry> sets)
    {
        var counted = sets.Where(s => s.SetType != SetType.Warmup).ToList();
        if (counted.Count == 0)
        {
            return null;
        }

        var weights = counted.Where(s => s.WeightKg is > 0).Select(s => s.WeightKg!.Value).ToList();
        var volumes = counted.Where(s => s.Reps is > 0 && s.WeightKg is > 0)
            .Select(s => s.Reps!.Value * s.WeightKg!.Value).ToList();
        var reps = counted.Where(s => s.Reps is > 0).Select(s => s.Reps!.Value).ToList();
        var durations = counted.Where(s => s.DurationSeconds is > 0).Select(s => s.DurationSeconds!.Value).ToList();
        var distances = counted.Where(s => s.DistanceM is > 0).Select(s => s.DistanceM!.Value).ToList();

        if (weights.Count + reps.Count + durations.Count + distances.Count == 0)
        {
            return null;
        }

        return new ProgressPoint(
            day,
            counted.Count,
            weights.Count > 0 ? weights.Max() : null,
            volumes.Count > 0 ? volumes.Max() : null,
            volumes.Count > 0 ? volumes.Sum() : null,
            reps.Count > 0 ? reps.Max() : null,
            reps.Count > 0 ? reps.Sum() : null,
            durations.Count > 0 ? durations.Max() : null,
            durations.Count > 0 ? durations.Sum() : null,
            distances.Count > 0 ? distances.Max() : null,
            distances.Count > 0 ? distances.Sum() : null);
    }

    /// <summary>
    /// Récords sobre los puntos. Según el <paramref name="tracking"/> salen unos kinds u otros. Si dos días empatan en
    /// el valor máximo, gana el primero (el récord se logró entonces).
    /// </summary>
    public static IReadOnlyList<PersonalRecordItem> Records(TrackingType tracking, IReadOnlyList<ProgressPoint> points)
    {
        var result = new List<PersonalRecordItem>();

        void Add(string kind, string unit, Func<ProgressPoint, decimal?> pick)
        {
            ProgressPoint? best = null;
            decimal? bestValue = null;
            foreach (var point in points.OrderBy(p => p.PerformedOn))
            {
                var value = pick(point);
                if (value is { } v && (bestValue is null || v > bestValue))
                {
                    best = point;
                    bestValue = v;
                }
            }

            if (best is not null && bestValue is { } found)
            {
                result.Add(new PersonalRecordItem(kind, found, unit, best.PerformedOn));
            }
        }

        switch (tracking)
        {
            case TrackingType.RepsWeight:
                Add(RecordKinds.BestWeight, "kg", p => p.BestWeightKg);
                Add(RecordKinds.BestSetVolume, "kg", p => p.BestSetVolume);
                Add(RecordKinds.BestSessionVolume, "kg", p => p.TotalVolume);
                break;
            case TrackingType.Reps:
                Add(RecordKinds.BestReps, "reps", p => p.BestReps);
                Add(RecordKinds.BestSessionReps, "reps", p => p.TotalReps);
                break;
            case TrackingType.Time:
                Add(RecordKinds.BestDuration, "s", p => p.BestDurationSeconds);
                break;
            case TrackingType.Distance:
                Add(RecordKinds.BestDistance, "m", p => p.BestDistanceM);
                break;
            case TrackingType.TimeDistance:
                Add(RecordKinds.BestDistance, "m", p => p.BestDistanceM);
                Add(RecordKinds.BestDuration, "s", p => p.BestDurationSeconds);
                break;
            case TrackingType.WeightDistance:
                Add(RecordKinds.BestWeight, "kg", p => p.BestWeightKg);
                Add(RecordKinds.BestDistance, "m", p => p.BestDistanceM);
                break;
        }

        return result;
    }

    /// <summary>
    /// Récords de repeticiones con peso que el día de hoy mejora respecto a lo mejor de los días anteriores y a lo que
    /// ese mismo día ya tenía guardado (<paramref name="oldToday"/>). Sin marca anterior no hay récord que batir: es la
    /// línea base.
    /// </summary>
    public static IReadOnlyList<string> BrokenKinds(
        IReadOnlyList<ProgressPoint> previousDays, ProgressPoint? oldToday, ProgressPoint? newToday)
    {
        if (newToday is null || previousDays.Count == 0)
        {
            return [];
        }

        var kinds = new List<string>();

        void Check(string kind, Func<ProgressPoint, decimal?> pick)
        {
            var now = pick(newToday);
            if (now is null)
            {
                return;
            }

            var earlier = previousDays.Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList();
            if (earlier.Count == 0)
            {
                return; // el ejercicio no tenía esa medida antes: línea base
            }

            var before = oldToday is null ? null : pick(oldToday);
            if (now > earlier.Max() && (before is null || now > before))
            {
                kinds.Add(kind);
            }
        }

        Check(RecordKinds.BestWeight, p => p.BestWeightKg);
        Check(RecordKinds.BestSetVolume, p => p.BestSetVolume);
        Check(RecordKinds.BestSessionVolume, p => p.TotalVolume);
        return kinds;
    }
}
