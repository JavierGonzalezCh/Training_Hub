using GSM.Application.Common.Events;

namespace GSM.Application.Routines.Events;

public sealed record RoutineAssigned(
    Guid RoutineId, Guid MemberId, Guid GymId, bool SelfAssigned) : IDomainEvent;

/// <summary>
/// Solo la **primera** vez que el socio registra algo en un día dado, para contar sesiones y
/// no ejercicios sueltos.
/// </summary>
public sealed record RoutineSessionLogged(
    Guid MemberId, Guid GymId, DateOnly PerformedOn, bool SelfAssigned) : IDomainEvent;
