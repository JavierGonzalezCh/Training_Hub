namespace GSM.Application.Routines.Contracts;

/// <summary>Marcar y/o anotar carga: campos opcionales, un cuerpo vacío es "hecho".</summary>
public sealed record LogRoutineExerciseRequest(
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    IReadOnlyList<LogSetRequest>? Sets = null);

/// <summary>Una serie realizada: repeticiones y peso que usó el miembro. El número lo pone el servidor por orden.</summary>
public sealed record LogSetRequest(int? Reps, decimal? WeightKg);

public sealed record SetResponse(int Number, int? Reps, decimal? WeightKg);

public sealed record RoutineLogResponse(
    Guid Id,
    Guid RoutineExerciseId,
    DateOnly PerformedOn,
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SetResponse>? Sets = null);

public sealed record RoutineHistoryItem(
    DateOnly PerformedOn,
    Guid RoutineExerciseId,
    Guid ExerciseId,
    string ExerciseName,
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SetResponse>? Sets = null);
