namespace GSM.Application.Routines.Contracts;

/// <summary>
/// Un día de la semana de cumplimiento. <paramref name="Status"/>: <c>done</c> (tocaba y registró),
/// <c>missed</c> (tocaba, ya pasó y no registró), <c>today</c> (toca hoy y falta), <c>upcoming</c> (toca más
/// adelante), <c>rest</c> (descanso planificado), <c>offplan</c> (registró un día que no tocaba) o <c>none</c>.
/// </summary>
public sealed record AdherenceDayItem(
    DateOnly Date,
    string Status,
    string? DayName,
    int PlannedExercises,
    int DoneExercises);

/// <summary>
/// Una semana de negocio (lunes a domingo). <paramref name="CompliancePercent"/> = días hechos sobre los días que
/// ya tocaban (los futuros no cuentan en contra); <c>null</c> si en la semana no hubo días planeados. Los días
/// fuera de plan se cuentan aparte y no suben el porcentaje.
/// </summary>
/// <param name="PlannedDays">Días de entrenamiento de la semana completa.</param>
/// <param name="DueDays">De esos, los que ya pasaron o ya se hicieron: la base del porcentaje.</param>
/// <param name="SelfAssigned">Algún día planeado viene de una rutina que el propio miembro armó.</param>
/// <param name="Days">Detalle día por día; solo en la semana en curso.</param>
public sealed record AdherenceWeekItem(
    DateOnly WeekStart,
    int PlannedDays,
    int DoneDays,
    int DueDays,
    int PlannedExercises,
    int DoneExercises,
    int OffPlanDays,
    int? CompliancePercent,
    bool SelfAssigned,
    IReadOnlyList<AdherenceDayItem>? Days);

/// <param name="Weeks">De la más antigua a la semana en curso.</param>
public sealed record MemberAdherence(
    Guid MemberId,
    bool HasRoutine,
    IReadOnlyList<AdherenceWeekItem> Weeks);

/// <param name="CurrentWeekPercent">Cumplimiento de la semana pedida.</param>
/// <param name="RecentPercent">Cumplimiento de la semana pedida y la anterior juntas; base del orden y del filtro.</param>
/// <param name="LastLogOn">Último día con un ejercicio registrado; <c>null</c> si nunca registró.</param>
public sealed record MemberAdherenceRow(
    Guid MemberId,
    string FullName,
    Guid RoutineId,
    string RoutineName,
    bool SelfAssigned,
    DateOnly WeekStart,
    int PlannedDays,
    int DoneDays,
    int? CurrentWeekPercent,
    int? RecentPercent,
    DateOnly? LastLogOn);
