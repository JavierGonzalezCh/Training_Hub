namespace GSM.Application.Routines.Contracts;

public sealed record RoutineExerciseInput(
    Guid ExerciseId,
    int TargetSets,
    string TargetReps,
    decimal? TargetWeightKg,
    int? RestSeconds,
    string? Notes);

public sealed record RoutineDayInput(
    string? Name,
    bool IsRest,
    IReadOnlyList<RoutineExerciseInput> Exercises);

public sealed record SaveRoutineRequest(
    string Name,
    string? Notes,
    IReadOnlyList<RoutineDayInput> Days);

public sealed record RoutineExerciseResponse(
    Guid Id,
    Guid ExerciseId,
    string ExerciseName,
    int Order,
    int TargetSets,
    string TargetReps,
    decimal? TargetWeightKg,
    int? RestSeconds,
    string? Notes);

public sealed record RoutineDayResponse(
    Guid Id,
    int Position,
    string Name,
    bool IsRest,
    IReadOnlyList<RoutineExerciseResponse> Exercises);

public sealed record RoutineResponse(
    Guid Id,
    Guid MemberId,
    string Name,
    string? Notes,
    Guid AssignedBy,
    bool SelfAssigned,
    DateOnly StartsOn,
    bool IsActive,
    DateOnly? DeactivatedOn,
    int CycleLength,
    IReadOnlyList<RoutineDayResponse> Days);

public sealed record RoutineListItem(
    Guid Id,
    string Name,
    DateOnly StartsOn,
    bool IsActive,
    DateOnly? DeactivatedOn,
    int CycleLength);

/// <summary>Rutina activa del socio + qué toca hoy (o descanso).</summary>
public sealed record MyRoutineResponse(
    RoutineResponse? Routine,
    TodayView? Today);

public sealed record TodayView(
    DateOnly Date,
    int Position,
    string DayName,
    bool IsRest,
    IReadOnlyList<RoutineExerciseResponse> Exercises);
