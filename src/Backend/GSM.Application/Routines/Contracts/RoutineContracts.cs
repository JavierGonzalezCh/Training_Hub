namespace GSM.Application.Routines.Contracts;

public sealed record RoutineExerciseInput(
    Guid ExerciseId,
    int TargetSets,
    string TargetReps,
    decimal? TargetWeightKg,
    int? RestSeconds,
    string? Notes,
    Guid? Id = null);

public sealed record RoutineDayInput(
    string? Name,
    bool IsRest,
    IReadOnlyList<RoutineExerciseInput> Exercises,
    Guid? Id = null,
    int? DayOfWeek = null);

/// <summary>
/// <c>Id</c> en un día o una línea = existente que se actualiza; sin <c>Id</c> = nuevo. Lo que existía y no
/// viene en el body se retira (sus registros se conservan). Solo aplica a <c>PUT</c>; al crear se ignoran.
/// <para>
/// <c>ScheduleMode</c> es <c>"rotating"</c> (por defecto si falta) o <c>"weekly"</c>. En semanal cada día lleva
/// <c>DayOfWeek</c> (1 = lunes … 7 = domingo, sin repetir) y no hay días de descanso; en rotativo no lleva
/// <c>DayOfWeek</c>. Si en un <c>PUT</c> viene un modo distinto del de la rutina, no es una edición: se crea una
/// rutina nueva, la anterior se desactiva y la respuesta trae el <c>Id</c> de la nueva.
/// </para>
/// </summary>
public sealed record SaveRoutineRequest(
    string Name,
    string? Notes,
    IReadOnlyList<RoutineDayInput> Days,
    string? ScheduleMode = null);

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
    IReadOnlyList<RoutineExerciseResponse> Exercises,
    int? DayOfWeek = null);

/// <summary>
/// <c>ScheduleMode</c>: <c>"rotating"</c> o <c>"weekly"</c> (texto, no enum: el generador de NSwag tipa los enums
/// sueltos de un cuerpo como <c>int</c>). <c>CycleLength</c> es el número de días vigentes: en semanal, los de entrenamiento.
/// </summary>
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
    IReadOnlyList<RoutineDayResponse> Days,
    string ScheduleMode);

public sealed record RoutineListItem(
    Guid Id,
    string Name,
    DateOnly StartsOn,
    bool IsActive,
    DateOnly? DeactivatedOn,
    int CycleLength,
    string ScheduleMode);

/// <summary>Rutina activa del socio + qué toca hoy (o descanso).</summary>
public sealed record MyRoutineResponse(
    RoutineResponse? Routine,
    TodayView? Today);

/// <summary>
/// En modo semanal <c>Position</c> es 0 y <c>DayName</c> "Descanso" cuando hoy no tiene fila (descanso).
/// <c>DayOfWeek</c> es el de hoy (1 = lunes … 7 = domingo).
/// </summary>
public sealed record TodayView(
    DateOnly Date,
    int Position,
    string DayName,
    bool IsRest,
    IReadOnlyList<RoutineExerciseResponse> Exercises,
    int DayOfWeek);
