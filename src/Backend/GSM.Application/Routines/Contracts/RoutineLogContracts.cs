namespace GSM.Application.Routines.Contracts;

/// <summary>
/// Marcar el ejercicio y/o anotar sus series: un cuerpo vacío es "hecho". Repetir el mismo día reemplaza el detalle.
/// <c>ReplacementExerciseId</c> ("Hacer otro ejercicio hoy", R5): el ejercicio que el miembro hizo en lugar del
/// planeado, solo ese día; debe estar en la lista de alternativas del planeado (si no, 422). Sin valor, o igual al
/// planeado, se registra el planeado. La rutina no cambia.
/// </summary>
public sealed record LogRoutineExerciseRequest(
    string? Notes,
    IReadOnlyList<LogSetRequest>? Sets = null,
    Guid? ReplacementExerciseId = null);

/// <summary>
/// Una serie realizada. <c>SetType</c>: <c>"normal"</c> (por defecto si falta), <c>"warmup"</c>, <c>"drop"</c> o
/// <c>"failure"</c>. El número lo pone el servidor por orden. En ejercicios unilaterales las repeticiones son por lado.
/// </summary>
public sealed record LogSetRequest(
    string? SetType,
    int? Reps,
    decimal? WeightKg,
    int? DurationSeconds,
    int? DistanceM,
    int? Rir);

public sealed record SetResponse(
    int Number,
    string SetType,
    int? Reps,
    decimal? WeightKg,
    int? DurationSeconds,
    int? DistanceM,
    int? Rir);

public sealed record RoutineLogResponse(
    Guid Id,
    Guid RoutineExerciseId,
    DateOnly PerformedOn,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SetResponse>? Sets = null,
    Guid? ExerciseId = null);

/// <summary>
/// <c>ExerciseId</c>/<c>ExerciseName</c> son los del ejercicio que de verdad se hizo. Si fue un reemplazo del día
/// (R5), <c>PlannedExerciseId</c>/<c>PlannedExerciseName</c> traen el planeado ("Hizo X en lugar de Y"); si no, son nulos.
/// </summary>
public sealed record RoutineHistoryItem(
    DateOnly PerformedOn,
    Guid RoutineExerciseId,
    Guid ExerciseId,
    string ExerciseName,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SetResponse>? Sets = null,
    Guid? PlannedExerciseId = null,
    string? PlannedExerciseName = null);
