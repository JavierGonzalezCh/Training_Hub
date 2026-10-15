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

    /// <summary>
    /// Series planeadas una por una (JSON en la misma fila). Solo traen los campos que corresponden al
    /// <c>tracking_type</c> del ejercicio.
    /// </summary>
    public List<SetTarget> SetTargets { get; private set; } = [];

    /// <summary>
    /// Las líneas del mismo día con el mismo grupo forman una superserie o circuito y se hacen seguidas.
    /// <c>null</c> = ejercicio suelto.
    /// </summary>
    public int? Group { get; private set; }

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

        if (draft.SetTargets is null || draft.SetTargets.Count == 0)
        {
            throw new ValidationDomainException("La línea necesita al menos una serie.");
        }

        if (draft.SetTargets.Count > SetTargetRules.MaxSets)
        {
            throw new ValidationDomainException($"Una línea admite como máximo {SetTargetRules.MaxSets} series.");
        }

        if (draft.Group is < 1)
        {
            throw new ValidationDomainException("El grupo de superserie debe ser un número positivo.");
        }

        if (draft.RestSeconds is < 0)
        {
            throw new ValidationDomainException("rest_seconds no puede ser negativo.");
        }

        ExerciseId = draft.ExerciseId;
        Order = order;
        SetTargets = draft.SetTargets.Select((t, i) => t with { Number = i + 1 }).ToList();
        Group = draft.Group;
        RestSeconds = draft.RestSeconds;
        Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();
    }
}
