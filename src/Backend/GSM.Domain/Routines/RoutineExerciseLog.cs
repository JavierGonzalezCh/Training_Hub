using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Registro de que el miembro hizo un ejercicio un día. Marcar y anotar las series son la misma
/// operación: un registro sin series es simplemente "hecho".
/// Único por <c>(routine_exercise_id, performed_on)</c>: repetir **actualiza**, no duplica.
/// </summary>
public sealed class RoutineExerciseLog : BaseEntity, ITenantEntity
{
    private RoutineExerciseLog()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Denormalizado: casi toda consulta de progreso parte del miembro.</summary>
    public Guid MemberId { get; private set; }

    public Guid RoutineExerciseId { get; private set; }

    /// <summary>
    /// Ejercicio del catálogo que se hizo, copiado de la línea al registrar. Permite consultar el
    /// historial de un ejercicio aunque la línea se haya retirado o la rutina haya cambiado.
    /// </summary>
    public Guid ExerciseId { get; private set; }

    /// <summary>Día de negocio, no UTC.</summary>
    public DateOnly PerformedOn { get; private set; }

    /// <summary>Instante real del registro.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// Detalle por serie, simétrico con <c>set_targets</c> de la línea. Vacío si el miembro solo marcó el
    /// ejercicio como hecho.
    /// </summary>
    public List<RoutineSetEntry> Sets { get; private set; } = [];

    public const int MaxSets = 30;

    public static RoutineExerciseLog Record(
        Guid gymId,
        Guid memberId,
        Guid routineExerciseId,
        Guid exerciseId,
        DateOnly performedOn,
        DateTimeOffset completedAt,
        string? notes,
        IReadOnlyList<RoutineSetEntry>? sets = null)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || routineExerciseId == Guid.Empty || exerciseId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de registro incompletos.");
        }

        var log = new RoutineExerciseLog
        {
            GymId = gymId,
            MemberId = memberId,
            RoutineExerciseId = routineExerciseId,
            ExerciseId = exerciseId,
            PerformedOn = performedOn,
            CompletedAt = completedAt,
        };

        log.SetLoad(notes, sets);
        return log;
    }

    /// <summary>Segunda marca del mismo día: se reemplazan el detalle y el instante.</summary>
    public void Update(DateTimeOffset completedAt, string? notes, IReadOnlyList<RoutineSetEntry>? sets = null)
    {
        CompletedAt = completedAt;
        SetLoad(notes, sets);
    }

    private void SetLoad(string? notes, IReadOnlyList<RoutineSetEntry>? sets)
    {
        var detail = NormalizeSets(sets);

        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (cleanNotes is { Length: > 500 })
        {
            throw new ValidationDomainException("Las notas no pueden exceder 500 caracteres.");
        }

        Sets = detail;
        Notes = cleanNotes;
    }

    private static List<RoutineSetEntry> NormalizeSets(IReadOnlyList<RoutineSetEntry>? sets)
    {
        if (sets is null || sets.Count == 0)
        {
            return [];
        }

        if (sets.Count > MaxSets)
        {
            throw new ValidationDomainException($"Un ejercicio admite como máximo {MaxSets} series.");
        }

        var result = new List<RoutineSetEntry>(sets.Count);
        foreach (var set in sets)
        {
            if (set.Reps is < 0 or > 1000)
            {
                throw new ValidationDomainException("Las repeticiones de una serie deben estar entre 0 y 1000.");
            }

            if (set.WeightKg is < 0 or > 1000)
            {
                throw new ValidationDomainException("El peso de una serie debe estar entre 0 y 1000 kg.");
            }

            if (set.DurationSeconds is < 0 or > 86400)
            {
                throw new ValidationDomainException("La duración de una serie debe estar entre 0 y 86400 segundos.");
            }

            if (set.DistanceM is < 0 or > 1_000_000)
            {
                throw new ValidationDomainException("La distancia de una serie debe estar entre 0 y 1000000 m.");
            }

            if (set.Rir is < 0 or > 10)
            {
                throw new ValidationDomainException("El RIR debe estar entre 0 y 10.");
            }

            result.Add(set with { Number = result.Count + 1 });
        }

        return result;
    }
}
