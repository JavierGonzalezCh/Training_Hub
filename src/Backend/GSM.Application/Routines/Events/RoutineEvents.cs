using GSM.Application.Common.Events;

namespace GSM.Application.Routines.Events;

public sealed record RoutineAssigned(
    Guid RoutineId, Guid MemberId, Guid GymId, bool SelfAssigned) : IDomainEvent;

/// <summary>
/// Solo la **primera** vez que el miembro registra algo en un día dado, para contar sesiones y
/// no ejercicios sueltos.
/// </summary>
public sealed record RoutineSessionLogged(
    Guid MemberId, Guid GymId, DateOnly PerformedOn, bool SelfAssigned) : IDomainEvent;

/// <summary>
/// El miembro registró un ejercicio nuevo hoy (no una edición del registro de ese día). A diferencia de
/// <see cref="RoutineSessionLogged"/> se publica con cada ejercicio, no solo con el primero: Attendance lo usa para contar el
/// día cuando el primer ejercicio no pudo (membresía vencida que luego se renovó). Los consumidores deben ser idempotentes.
/// </summary>
public sealed record RoutineExerciseLogged(
    Guid MemberId, Guid GymId, DateOnly PerformedOn, bool SelfAssigned) : IDomainEvent;

/// <summary>
/// El miembro superó, al guardar un registro, su mejor marca anterior en un ejercicio de repeticiones con peso (R7).
/// <c>Kinds</c> son los récords batidos: <c>best_weight</c>, <c>best_set_volume</c> (peso × repeticiones de la mejor
/// serie) y/o <c>best_session_volume</c> (Σ peso × repeticiones del día). Solo se publica cuando hay una marca
/// anterior que superar (la primera vez es la línea base) y solo si el registro mejora lo ya guardado ese día.
/// Lo consumirá Achievements (L3); <c>SelfAssigned</c> permite ignorar las rutinas creadas por el propio miembro.
/// Idempotencia del consumidor: <c>(MemberId, ExerciseId, PerformedOn, Kind)</c>.
/// </summary>
public sealed record PersonalRecordSet(
    Guid MemberId,
    Guid GymId,
    Guid ExerciseId,
    DateOnly PerformedOn,
    bool SelfAssigned,
    IReadOnlyList<string> Kinds) : IDomainEvent;
