using GSM.Domain.Routines;

namespace GSM.Application.Routines.Abstractions;

/// <summary>Referencia resuelta de una línea de rutina a su rutina/socio (para autorizar el registro).</summary>
public sealed record RoutineExerciseRef(Guid RoutineExerciseId, Guid GymId, Guid MemberId, bool SelfAssigned);

public sealed record RoutineHistoryRow(
    DateOnly PerformedOn,
    Guid RoutineExerciseId,
    Guid ExerciseId,
    string ExerciseName,
    int? SetsDone,
    string? RepsDone,
    decimal? WeightKg,
    string? Notes,
    DateTimeOffset CompletedAt);

public interface IRoutineLogRepository
{
    Task<RoutineExerciseRef?> GetExerciseRefAsync(
        Guid routineExerciseId, CancellationToken cancellationToken = default);

    Task<RoutineExerciseLog?> GetAsync(
        Guid routineExerciseId, DateOnly performedOn, CancellationToken cancellationToken = default);

    /// <summary>¿El socio ya tenía algún registro ese día (antes de este)?</summary>
    Task<bool> MemberHasLogsOnAsync(
        Guid memberId, DateOnly performedOn, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoutineHistoryRow>> GetHistoryAsync(
        Guid memberId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    void Add(RoutineExerciseLog log);

    void Remove(RoutineExerciseLog log);
}
