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

    /// <summary>Día de negocio, no UTC.</summary>
    public DateOnly PerformedOn { get; private set; }

    /// <summary>Instante real del registro.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    public int? SetsDone { get; private set; }

    public string? RepsDone { get; private set; }

    public decimal? WeightKg { get; private set; }

    public string? Notes { get; private set; }

    public static RoutineExerciseLog Record(
        Guid gymId,
        Guid memberId,
        Guid routineExerciseId,
        DateOnly performedOn,
        DateTimeOffset completedAt,
        int? setsDone,
        string? repsDone,
        decimal? weightKg,
        string? notes)
    {
        if (gymId == Guid.Empty || memberId == Guid.Empty || routineExerciseId == Guid.Empty)
        {
            throw new ValidationDomainException("Datos de registro incompletos.");
        }

        var log = new RoutineExerciseLog
        {
            GymId = gymId,
            MemberId = memberId,
            RoutineExerciseId = routineExerciseId,
            PerformedOn = performedOn,
            CompletedAt = completedAt,
        };

        log.SetLoad(setsDone, repsDone, weightKg, notes);
        return log;
    }

    /// <summary>Segunda marca del mismo día: se actualizan carga e instante.</summary>
    public void Update(
        DateTimeOffset completedAt, int? setsDone, string? repsDone, decimal? weightKg, string? notes)
    {
        CompletedAt = completedAt;
        SetLoad(setsDone, repsDone, weightKg, notes);
    }

    private void SetLoad(int? setsDone, string? repsDone, decimal? weightKg, string? notes)
    {
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

        SetsDone = setsDone;
        RepsDone = reps;
        WeightKg = weightKg;
        Notes = cleanNotes;
    }
}
