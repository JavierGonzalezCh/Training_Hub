using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>
/// Registro de que el socio hizo un ejercicio un día. Marcar y anotar la carga son la misma
/// operación: un registro con los tres campos de carga en <c>null</c> es simplemente "hecho".
/// Único por <c>(routine_exercise_id, performed_on)</c>: repetir **actualiza**, no duplica.
/// </summary>
public sealed class RoutineExerciseLog : BaseEntity, ITenantEntity
{
    private RoutineExerciseLog()
    {
    }

    public Guid GymId { get; private set; }

    /// <summary>Denormalizado: casi toda consulta de progreso parte del socio.</summary>
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

    public int? SetsDone { get; private set; }

    public string? RepsDone { get; private set; }

    public decimal? WeightKg { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// Detalle por serie (repeticiones y peso de cada una). Vacío si el socio solo marcó el ejercicio o
    /// anotó una carga global. <see cref="SetsDone"/>, <see cref="RepsDone"/> y <see cref="WeightKg"/>
    /// se derivan de aquí cuando hay detalle, para que el progreso siga leyendo los mismos campos.
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
        int? setsDone,
        string? repsDone,
        decimal? weightKg,
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

        log.SetLoad(setsDone, repsDone, weightKg, notes, sets);
        return log;
    }

    /// <summary>Segunda marca del mismo día: se actualizan carga e instante.</summary>
    public void Update(
        DateTimeOffset completedAt, int? setsDone, string? repsDone, decimal? weightKg, string? notes,
        IReadOnlyList<RoutineSetEntry>? sets = null)
    {
        CompletedAt = completedAt;
        SetLoad(setsDone, repsDone, weightKg, notes, sets);
    }

    private void SetLoad(
        int? setsDone, string? repsDone, decimal? weightKg, string? notes, IReadOnlyList<RoutineSetEntry>? sets)
    {
        var detail = NormalizeSets(sets);
        if (detail.Count > 0)
        {
            // Con detalle por serie, los campos globales salen de él.
            setsDone = detail.Count;
            weightKg = detail.Where(s => s.WeightKg is not null).Select(s => s.WeightKg).DefaultIfEmpty(weightKg).Max();
            repsDone = string.IsNullOrWhiteSpace(repsDone) ? SummarizeReps(detail) : repsDone;
        }

        if (setsDone is < 0)
        {
            throw new ValidationDomainException("sets_done no puede ser negativo.");
        }

        if (weightKg is < 0)
        {
            throw new ValidationDomainException("weight_kg no puede ser negativo.");
        }

        var reps = string.IsNullOrWhiteSpace(repsDone) ? null : repsDone.Trim();
        if (reps is { Length: > 30 })
        {
            throw new ValidationDomainException("reps_done no puede exceder 30 caracteres.");
        }

        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (cleanNotes is { Length: > 500 })
        {
            throw new ValidationDomainException("Las notas no pueden exceder 500 caracteres.");
        }

        Sets = detail;
        SetsDone = setsDone;
        RepsDone = reps;
        WeightKg = weightKg;
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
            throw new ValidationDomainException($"Una serie de ejercicio admite como máximo {MaxSets} series.");
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

            result.Add(new RoutineSetEntry(result.Count + 1, set.Reps, set.WeightKg));
        }

        return result;
    }

    // "10" si todas iguales, "10/8/8" si varían (cabe en los 30 caracteres de reps_done).
    private static string? SummarizeReps(List<RoutineSetEntry> sets)
    {
        var reps = sets.Where(s => s.Reps is not null).Select(s => s.Reps!.Value).ToList();
        if (reps.Count == 0)
        {
            return null;
        }

        var text = reps.Distinct().Count() == 1 ? reps[0].ToString() : string.Join('/', reps);
        return text.Length <= 30 ? text : null;
    }
}
