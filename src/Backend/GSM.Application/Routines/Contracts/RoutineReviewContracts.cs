namespace GSM.Application.Routines.Contracts;

/// <summary>Semanas entre la asignación (o la última revisión) y la fecha de revisión de una rutina. 1 a 52; 6 por defecto.</summary>
public sealed record RoutineSettingsResponse(int ReviewWeeks);

public sealed record UpdateRoutineSettingsRequest(int ReviewWeeks);

/// <summary>
/// Una rutina que toca revisar. <c>ReviewOn</c> puede estar en el pasado (atrasada) o en los próximos días.
/// <c>LastReviewedAt</c> es nulo si nunca se ha revisado.
/// </summary>
public sealed record RoutineDueItem(
    Guid RoutineId,
    Guid MemberId,
    string MemberName,
    string RoutineName,
    DateOnly StartsOn,
    DateOnly ReviewOn,
    DateTimeOffset? LastReviewedAt);

/// <summary>
/// Rutinas por revisar, la más atrasada primero. <c>Mine</c> dice qué filtro se aplicó: <c>true</c> = solo los miembros
/// cuyo entrenador asignado es quien consulta. <c>WindowDays</c> son los días hacia adelante que se incluyen.
/// </summary>
public sealed record DueRoutinesResponse(
    IReadOnlyList<RoutineDueItem> Items,
    bool Mine,
    int WindowDays);

/// <summary>Resultado de marcar una rutina como revisada: la nueva fecha de revisión.</summary>
public sealed record RoutineReviewResponse(Guid RoutineId, DateOnly ReviewOn, DateTimeOffset LastReviewedAt);
