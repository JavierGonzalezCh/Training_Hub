using GSM.Domain.Common;
using GSM.Domain.Common.Exceptions;

namespace GSM.Domain.Routines;

/// <summary>Una línea de ejercicio dentro de un día de la rutina.</summary>
public sealed class RoutineExercise : BaseEntity, ITenantEntity
{
    private RoutineExercise()
    {
    }

    public Guid GymId { get; private set; }

    public Guid RoutineDayId { get; private set; }

    /// <summary>Referencia al catálogo. Sin FK (el catálogo admite filas globales); se valida en el caso de uso.</summary>
    public Guid ExerciseId { get; private set; }

    public int Order { get; private set; }

    public int TargetSets { get; private set; }

    public string TargetReps { get; private set; } = string.Empty;

    public decimal? TargetWeightKg { get; private set; }

    public int? RestSeconds { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// Día de negocio en que la línea salió de la rutina. Una línea retirada no se borra: sus registros
    /// de entrenamiento siguen ligados a ella.
    /// </summary>
    public DateOnly? RetiredOn { get; private set; }

    internal static RoutineExercise Create(Guid gymId, int order, RoutineExerciseDraft draft)
    {
        var exercise = new RoutineExercise { GymId = gymId };
        exercise.Apply(order, draft);
        return exercise;
    }

    /// <summary>Actualiza la línea en el lugar: conserva el id (y por tanto sus registros).</summary>
    internal void Update(int order, RoutineExerciseDraft draft) => Apply(order, draft);

    internal void Retire(DateOnly on) => RetiredOn ??= on;

    private void Apply(int order, RoutineExerciseDraft draft)
    {
        if (draft.ExerciseId == Guid.Empty)
        {
            throw new ValidationDomainException("La línea necesita un ejercicio.");
        }

        if (draft.TargetSets < 1)
        {
            throw new ValidationDomainException("target_sets debe ser al menos 1.");
        }

        var reps = (draft.TargetReps ?? string.Empty).Trim();
        if (reps.Length is 0 or > 30)
        {
            throw new ValidationDomainException("target_reps es obligatorio y no puede exceder 30 caracteres.");
        }

        if (draft.TargetWeightKg is < 0)
        {
            throw new ValidationDomainException("target_weight_kg no puede ser negativo.");
        }

        if (draft.RestSeconds is < 0)
        {
            throw new ValidationDomainException("rest_seconds no puede ser negativo.");
        }

        ExerciseId = draft.ExerciseId;
        Order = order;
        TargetSets = draft.TargetSets;
        TargetReps = reps;
        TargetWeightKg = draft.TargetWeightKg;
        RestSeconds = draft.RestSeconds;
        Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();
    }
}
