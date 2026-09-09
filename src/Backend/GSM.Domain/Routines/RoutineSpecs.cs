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

public sealed record RoutineDayDraft(
    string? Name,
    bool IsRest,
    IReadOnlyList<RoutineExerciseDraft> Exercises);

public sealed record RoutineExerciseDraft(
    Guid ExerciseId,
    int TargetSets,
    string TargetReps,
    decimal? TargetWeightKg,
    int? RestSeconds,
    string? Notes);
