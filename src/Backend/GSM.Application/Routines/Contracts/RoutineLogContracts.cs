namespace GSM.Application.Routines.Contracts;

/// <summary>
/// Marcar el ejercicio y/o anotar sus series: un cuerpo vacío es "hecho". Repetir el mismo día reemplaza el detalle.
/// </summary>
public sealed record LogRoutineExerciseRequest(
    string? Notes,
    IReadOnlyList<LogSetRequest>? Sets = null);

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
    IReadOnlyList<SetResponse>? Sets = null);

public sealed record RoutineHistoryItem(
    DateOnly PerformedOn,
    Guid RoutineExerciseId,
    Guid ExerciseId,
    string ExerciseName,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SetResponse>? Sets = null);
