using GSM.Application.Routines.Contracts;
using GSM.Application.Routines.Abstractions;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>Una alternativa resuelta: el ejercicio, de dónde sale y si se puede hacer hoy en el gym.</summary>
public sealed record ExerciseAlternativeCandidate(
    Exercise Exercise, string Source, ExerciseEquipmentInfo? Equipment);

/// <summary>
/// "¿Qué otro ejercicio puedo hacer en lugar de este?" (<c>GSM-modulo-routines.md</c>, Alternativas). Todo ejercicio
/// sugiere alternativas, aunque nadie las haya definido. Dos fuentes, en este orden:
/// <list type="number">
///   <item><b>Recomendadas</b> (<see cref="Source.Recommended"/>): las guardadas en <c>exercise_alternatives</c> por el
///   SuperAdmin o por el gym. Se excluyen las inactivas y las ocultas por el gym; <c>IsAvailableHere</c> las marca si
///   hoy no se pueden hacer, pero no las quita.</item>
///   <item><b>Sugeridas</b> (<see cref="Source.Suggested"/>), calculadas al vuelo: mismo músculo principal y mismo
///   tipo; orden: más músculos secundarios en común, misma medición, nivel igual o menor, nombre. Se excluyen las
///   inactivas, las ocultas, las que hoy no se pueden hacer en el gym y el propio ejercicio. Si salen menos de
///   <see cref="MinSuggested"/>, se completa con el mismo grupo muscular y tipo.</item>
/// </list>
/// Como máximo <see cref="MaxResults"/> entre las dos fuentes. Lo usan la lista de alternativas, "Cambiar por…" y la
/// validación de "Hacer otro ejercicio hoy" (el miembro solo elige de esta lista).
/// </summary>
public sealed class ExerciseAlternativesResolver(
    IExerciseRepository exercises,
    IExerciseAlternativeRepository alternatives,
    ExerciseEquipmentResolver equipment)
{
    public const int MaxResults = 6;
    public const int MinSuggested = 3;

    public static class Source
    {
        public const string Recommended = "recommended";
        public const string Suggested = "suggested";
    }

    public async Task<IReadOnlyList<ExerciseAlternativeCandidate>> ResolveAsync(
        Exercise source, CancellationToken cancellationToken)
    {
        var pairs = await alternatives.ListForAsync(source.Id, cancellationToken);
        var recommendedIds = pairs.Select(p => p.Other(source.Id)).Distinct().ToList();

        var recommendedExercises = await exercises.ListVisibleByIdsAsync(recommendedIds, cancellationToken);
        var hidden = await exercises.GetHiddenIdsAsync(recommendedIds, cancellationToken);
        var recommended = recommendedExercises
            .Where(e => e.IsActive && !hidden.Contains(e.Id) && e.Id != source.Id)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var taken = recommended.Select(e => e.Id).ToHashSet();
        taken.Add(source.Id);

        // Mismo músculo principal y mismo tipo. El listado ya deja fuera inactivos y ocultos del gym.
        var sameMuscle = await exercises.ListVisibleAsync(
            new ExerciseFilter(null, source.PrimaryMuscle, null, false, ExerciseType: source.ExerciseType),
            cancellationToken);
        var primary = sameMuscle.Where(e => !taken.Contains(e.Id)).ToList();

        // Solo hace falta el grupo muscular si lo anterior no alcanza; se calcula con la misma disponibilidad.
        var group = new List<Exercise>();
        var knownIds = primary.Select(e => e.Id).ToHashSet();
        IReadOnlyList<Exercise>? sameGroup = null;

        var needs = recommended.Concat(primary).Select(ExerciseNeed.Of).ToList();
        var availability = new Dictionary<Guid, ExerciseEquipmentInfo>(
            await equipment.ResolveAsync(needs, cancellationToken));

        bool CanDo(Exercise e) => !availability.TryGetValue(e.Id, out var info) || info.IsAvailable;

        var suggested = Rank(source, primary.Where(CanDo)).ToList();

        if (suggested.Count < MinSuggested)
        {
            sameGroup = await exercises.ListVisibleAsync(
                new ExerciseFilter(null, null, null, false, MuscleGroup: source.MuscleGroup, ExerciseType: source.ExerciseType),
                cancellationToken);
            group = sameGroup.Where(e => !taken.Contains(e.Id) && !knownIds.Contains(e.Id)).ToList();

            var extra = await equipment.ResolveAsync(group.Select(ExerciseNeed.Of).ToList(), cancellationToken);
            foreach (var (id, info) in extra)
            {
                availability[id] = info;
            }

            suggested.AddRange(Rank(source, group.Where(CanDo)).Take(MinSuggested - suggested.Count));
        }

        return recommended
            .Select(e => new ExerciseAlternativeCandidate(e, Source.Recommended, availability.GetValueOrDefault(e.Id)))
            .Concat(suggested.Select(e =>
                new ExerciseAlternativeCandidate(e, Source.Suggested, availability.GetValueOrDefault(e.Id))))
            .Take(MaxResults)
            .ToList();
    }

    /// <summary>Más músculos secundarios en común → misma medición → nivel igual o menor → nombre.</summary>
    private static IEnumerable<Exercise> Rank(Exercise source, IEnumerable<Exercise> candidates)
        => candidates
            .OrderByDescending(e => e.SecondaryMuscles.Intersect(source.SecondaryMuscles).Count())
            .ThenBy(e => e.TrackingType == source.TrackingType ? 0 : 1)
            .ThenBy(e => e.Difficulty <= source.Difficulty ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
}
