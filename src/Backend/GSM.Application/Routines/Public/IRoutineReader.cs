namespace GSM.Application.Routines.Public;

/// <summary>
/// Qué días tocaba entrenar y qué días eran descanso planificado en una ventana, resolviendo la
/// rutina vigente en cada fecha (<c>starts_on</c> / <c>deactivated_on</c>).
/// </summary>
public sealed record RoutineTrainingCalendar(
    IReadOnlySet<DateOnly> TrainingDays,
    IReadOnlySet<DateOnly> RestDays);

/// <summary>Lectura que el módulo Routines expone a los demás módulos (Attendance vía compositor, Achievements).</summary>
public interface IRoutineReader
{
    Task<int> GetActiveRoutineCountAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>starts_on</c> de la rutina activa del socio, o <c>null</c> si no tiene.
    /// Con <paramref name="onlyAssigned"/> ignora las que el socio se asignó a sí mismo.
    /// </summary>
    Task<DateOnly?> GetActiveRoutineStartAsync(
        Guid memberId, bool onlyAssigned = false, CancellationToken cancellationToken = default);

    Task<RoutineTrainingCalendar> GetTrainingCalendarAsync(
        Guid memberId, DateOnly from, DateOnly to,
        bool onlyAssigned = false, CancellationToken cancellationToken = default);

    /// <summary>Días distintos con al menos un ejercicio registrado. <paramref name="onlyAssigned"/> excluye las auto-asignadas.</summary>
    Task<int> GetCompletedSessionCountAsync(
        Guid memberId, bool onlyAssigned, CancellationToken cancellationToken = default);
}
