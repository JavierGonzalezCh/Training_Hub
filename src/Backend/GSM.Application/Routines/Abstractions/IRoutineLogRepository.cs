using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Referencia resuelta de una línea de rutina a su rutina/miembro (para autorizar el registro).</summary>
public sealed record RoutineExerciseRef(
    Guid RoutineExerciseId, Guid GymId, Guid MemberId, bool SelfAssigned, Guid ExerciseId, bool IsRetired = false);

public sealed record RoutineHistoryRow(
    DateOnly PerformedOn,
    Guid RoutineExerciseId,
    Guid ExerciseId,
    string ExerciseName,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<RoutineSetEntry>? Sets = null,
    Guid PlannedExerciseId = default,
    string PlannedExerciseName = "?");

/// <summary>Un registro reducido para el progreso (R7): qué ejercicio se hizo de verdad, qué día y con qué series.</summary>
public sealed record ProgressLogRow(
    DateOnly PerformedOn, Guid ExerciseId, bool SelfAssigned, IReadOnlyList<RoutineSetEntry> Sets,
    Guid RoutineExerciseId);

public interface IRoutineLogRepository
{
    /// <summary>
    /// Los registros del miembro (de un ejercicio concreto, o de todos), con sus series, sin rastrear. El ejercicio es el
    /// que se hizo, no el planeado. Incluye también los de rutinas ya desactivadas y de líneas retiradas.
    /// </summary>
    Task<IReadOnlyList<ProgressLogRow>> GetProgressLogsAsync(
        Guid memberId, Guid? exerciseId, CancellationToken cancellationToken = default);

    Task<RoutineExerciseRef?> GetExerciseRefAsync(
        Guid routineExerciseId, CancellationToken cancellationToken = default);

    Task<RoutineExerciseLog?> GetAsync(
        Guid routineExerciseId, DateOnly performedOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿El miembro ya tenía ese día algún registro de una rutina del mismo tipo
    /// (<paramref name="selfAssigned"/>)?
    /// </summary>
    /// <remarks>
    /// La distinción importa porque las sesiones auto-asignadas no puntúan: si se mirasen todos
    /// los registros del día sin distinguir, un primer registro en la rutina propia consumiría el
    /// "primero del día" y el registro posterior en la rutina asignada ya no publicaría evento,
    /// dejando los logros de esa jornada sin evaluar.
    /// </remarks>
    Task<bool> MemberHasLogsOnAsync(
        Guid memberId, DateOnly performedOn, bool selfAssigned, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoutineHistoryRow>> GetHistoryAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    void Add(RoutineExerciseLog log);

    void Remove(RoutineExerciseLog log);
}
