using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Objetivo de una serie planeada. Repeticiones como rango (<see cref="RepsMin"/>–<see cref="RepsMax"/>),
/// o ninguna con <see cref="SetType.Failure"/> ("al fallo"). <see cref="Rir"/> = repeticiones en reserva.
/// </summary>
public sealed record SetTarget(
    int Number,
    SetType SetType,
    int? RepsMin,
    int? RepsMax,
    decimal? WeightKg,
    int? DurationSeconds,
    int? DistanceM,
    int? Rir);

public static class SetTargetRules
{
    public const int MaxSets = 30;

    /// <summary>
    /// Deja solo los campos que corresponden al <paramref name="tracking"/> (el resto se descarta), valida
    /// rangos y renumera 1..n por orden. Un mínimo sin máximo se toma como máximo = mínimo.
    /// </summary>
    public static List<SetTarget> Normalize(TrackingType tracking, IReadOnlyList<SetTarget>? targets)
    {
        if (targets is null || targets.Count == 0)
        {
            throw new ValidationDomainException("La línea necesita al menos una serie.");
        }

        if (targets.Count > MaxSets)
        {
            throw new ValidationDomainException($"Una línea admite como máximo {MaxSets} series.");
        }

        var usesReps = tracking is TrackingType.RepsWeight or TrackingType.Reps;
        var usesWeight = tracking is TrackingType.RepsWeight;
        var usesTime = tracking is TrackingType.Time or TrackingType.TimeDistance;
        var usesDistance = tracking is TrackingType.Distance or TrackingType.TimeDistance;

        var result = new List<SetTarget>(targets.Count);
        foreach (var t in targets)
        {
            var min = usesReps ? t.RepsMin : null;
            var max = usesReps ? t.RepsMax : null;
            if (t.SetType == SetType.Failure)
            {
                // "Al fallo" no lleva rango de repeticiones.
                min = null;
                max = null;
            }

            if (min is null && max is not null)
            {
                min = max;
            }

            if (min is < 0 or > 1000 || max is < 0 or > 1000)
            {
                throw new ValidationDomainException("Las repeticiones de una serie deben estar entre 0 y 1000.");
            }

            if (min is not null && max is not null && max < min)
            {
                throw new ValidationDomainException("El máximo de repeticiones no puede ser menor que el mínimo.");
            }

            var weight = usesWeight ? t.WeightKg : null;
            if (weight is < 0 or > 1000)
            {
                throw new ValidationDomainException("El peso de una serie debe estar entre 0 y 1000 kg.");
            }

            var duration = usesTime ? t.DurationSeconds : null;
            if (duration is < 0 or > 86400)
            {
                throw new ValidationDomainException("La duración de una serie debe estar entre 0 y 86400 segundos.");
            }

            var distance = usesDistance ? t.DistanceM : null;
            if (distance is < 0 or > 1_000_000)
            {
                throw new ValidationDomainException("La distancia de una serie debe estar entre 0 y 1000000 m.");
            }

            if (t.Rir is < 0 or > 10)
            {
                throw new ValidationDomainException("El RIR debe estar entre 0 y 10.");
            }

            result.Add(new SetTarget(result.Count + 1, t.SetType, min, max ?? min, weight, duration, distance, t.Rir));
        }

        return result;
    }
}
