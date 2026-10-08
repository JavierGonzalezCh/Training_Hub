namespace GSM.Domain.Routines;

/// <summary>
/// Datos de entrada para construir/reemplazar una rutina. El caso de uso ya validó el socio y
/// cada <c>ExerciseId</c> contra el catálogo visible; el agregado valida sus propias invariantes.
/// </summary>
public sealed record RoutineDraft(
    string Name,
    string? Notes,
    Guid AssignedBy,
    bool SelfAssigned,
    IReadOnlyList<RoutineDayDraft> Days);

/// <summary><c>Id</c> nulo = día nuevo; con valor = el día existente que se actualiza.</summary>
public sealed record RoutineDayDraft(
    string? Name,
    bool IsRest,
    IReadOnlyList<RoutineExerciseDraft> Exercises,
    Guid? Id = null);

public sealed record RoutineExerciseDraft(
    Guid ExerciseId,
    int TargetSets,
    string TargetReps,
    decimal? TargetWeightKg,
    int? RestSeconds,
    string? Notes,
    Guid? Id = null);
