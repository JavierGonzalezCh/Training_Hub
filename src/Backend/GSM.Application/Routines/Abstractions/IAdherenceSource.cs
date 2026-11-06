using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Línea de un día de rutina, con la fecha en que salió de la rutina si ya no está.</summary>
public sealed record AdherenceLine(Guid Id, DateOnly? RetiredOn);

/// <summary>Día de una rutina con sus vigencias (R2b): cada fecha se juzga con los días que estaban ese día.</summary>
public sealed record AdherenceDay(
    Guid Id,
    int Position,
    bool IsRest,
    int? DayOfWeek,
    DateOnly AddedOn,
    DateOnly? RetiredOn,
    string? Name,
    IReadOnlyList<AdherenceLine> Lines);

public sealed record AdherenceRoutine(
    Guid MemberId,
    Guid Id,
    string Name,
    bool SelfAssigned,
    ScheduleMode Mode,
    DateOnly StartsOn,
    DateOnly? DeactivatedOn,
    bool IsActive,
    IReadOnlyList<AdherenceDay> Days);

/// <summary>Un ejercicio registrado: qué línea de la rutina y qué día de negocio.</summary>
public sealed record AdherenceLog(Guid MemberId, DateOnly PerformedOn, Guid RoutineExerciseId);

/// <summary>Ajuste del calendario de un miembro (SP): en <paramref name="Date"/> cuenta <paramref name="PerformedDayId"/>.</summary>
public sealed record AdherenceOverride(
    Guid MemberId, Guid RoutineId, DateOnly Date, Guid PlannedDayId, Guid PerformedDayId, RoutineDayOverrideReason Reason);

public sealed record AdherenceData(
    IReadOnlyList<AdherenceRoutine> Routines,
    IReadOnlyList<AdherenceLog> Logs,
    IReadOnlyList<AdherenceOverride>? Overrides = null)
{
    public IReadOnlyList<AdherenceOverride> OverridesOrEmpty => Overrides ?? [];
}

/// <summary>Lectura plana para el cumplimiento de la rutina (docs/GSM-tipos-de-negocio.md §6). Solo lectura.</summary>
public interface IAdherenceSource
{
    /// <summary>
    /// Rutinas que se cruzan con <c>[from, to]</c> de cada miembro, con todos sus días y líneas (también los
    /// retirados), los registros de ejercicio de esa ventana y los ajustes del calendario (SP) de esa ventana.
    /// </summary>
    Task<AdherenceData> LoadAsync(
        IReadOnlyCollection<Guid> memberIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Último día con algún ejercicio registrado, por miembro (los que nunca registraron no aparecen).</summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastLogDatesAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default);
}
