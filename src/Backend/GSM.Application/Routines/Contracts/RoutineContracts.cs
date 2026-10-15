namespace GSM.Application.Routines.Contracts;

/// <summary>
/// Objetivo de una serie. <c>SetType</c>: <c>"normal"</c> (por defecto si falta), <c>"warmup"</c>, <c>"drop"</c> o
/// <c>"failure"</c> (al fallo: sin repeticiones). Repeticiones como rango <c>RepsMin</c>–<c>RepsMax</c> (un solo extremo
/// se toma como ambos). Solo cuentan los campos que corresponden al <c>tracking_type</c> del ejercicio; el resto se
/// descarta. <c>Rir</c> = repeticiones en reserva (0 a 10). El número de la serie lo pone el servidor por orden.
/// En ejercicios unilaterales las repeticiones son por lado.
/// </summary>
public sealed record SetTargetInput(
    string? SetType,
    int? RepsMin,
    int? RepsMax,
    decimal? WeightKg,
    int? DurationSeconds,
    int? DistanceM,
    int? Rir);

/// <summary>
/// <c>SetTargets</c>: una entrada por serie planeada (al menos una, máximo 30). <c>Group</c>: las líneas de un mismo
/// día con el mismo número forman una superserie o circuito y deben ir seguidas; <c>null</c> = ejercicio suelto.
/// En un grupo, <c>RestSeconds</c> es el descanso tras completar la vuelta.
/// </summary>
public sealed record RoutineExerciseInput(
    Guid ExerciseId,
    IReadOnlyList<SetTargetInput> SetTargets,
    int? RestSeconds,
    string? Notes,
    int? Group = null,
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

public sealed record SetTargetResponse(
    int Number,
    string SetType,
    int? RepsMin,
    int? RepsMax,
    decimal? WeightKg,
    int? DurationSeconds,
    int? DistanceM,
    int? Rir);

/// <summary>
/// <c>TrackingType</c> (qué se mide: <c>RepsWeight</c>, <c>Reps</c>, <c>Time</c>, <c>Distance</c>, <c>TimeDistance</c>, <c>WeightDistance</c>),
/// <c>IsUnilateral</c> y <c>MuscleGroup</c> (<c>UpperBody</c>, <c>LowerBody</c>, <c>Core</c>, <c>FullBody</c>) los hereda
/// la línea de su ejercicio. <c>SetTargets</c> trae solo los campos que corresponden al <c>TrackingType</c>.
/// <c>Equipment</c>: disponibilidad y máquinas del ejercicio en el gym (foto, zona, instrucciones, estado);
/// <c>null</c> si el gym no tiene inventario o no se pudo resolver.
/// </summary>
public sealed record RoutineExerciseResponse(
    Guid Id,
    Guid ExerciseId,
    string ExerciseName,
    int Order,
    IReadOnlyList<SetTargetResponse> SetTargets,
    int? Group,
    int? RestSeconds,
    string? Notes,
    string TrackingType = "RepsWeight",
    bool IsUnilateral = false,
    string MuscleGroup = "FullBody",
    ExerciseEquipmentInfo? Equipment = null);

public sealed record RoutineDayResponse(
    Guid Id,
    int Position,
    string Name,
    bool IsRest,
    IReadOnlyList<RoutineExerciseResponse> Exercises,
    int? DayOfWeek = null,
    IReadOnlyList<string>? MuscleGroups = null);

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

/// <summary>Rutina activa del miembro + qué toca hoy (o descanso).</summary>
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
    int DayOfWeek,
    IReadOnlyList<string>? MuscleGroups = null);
