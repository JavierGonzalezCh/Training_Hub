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
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    DateTimeOffset CompletedAt,
    IReadOnlyList<RoutineSetEntry>? Sets = null);

public interface IRoutineLogRepository
{
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
