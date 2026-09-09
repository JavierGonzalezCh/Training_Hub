namespace GSM.Application.Routines.Contracts;

/// <summary>Marcar y/o anotar carga: campos opcionales, un cuerpo vacío es "hecho".</summary>
public sealed record LogRoutineExerciseRequest(
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes);

public sealed record RoutineLogResponse(
    Guid Id,
    Guid RoutineExerciseId,
    DateOnly PerformedOn,
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    DateTimeOffset CompletedAt);

public sealed record RoutineHistoryItem(
    DateOnly PerformedOn,
    Guid RoutineExerciseId,
    Guid ExerciseId,
    string ExerciseName,
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    DateTimeOffset CompletedAt);
