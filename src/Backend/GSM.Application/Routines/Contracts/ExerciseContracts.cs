using GSM.Domain.Routines;

namespace GSM.Application.Routines.Contracts;

public sealed record CreateExerciseRequest(
    string Name,
    string? Description,
    Muscle PrimaryMuscle,
    IReadOnlyList<Muscle>? SecondaryMuscles,
    IReadOnlyList<Equipment>? Equipment,
    string? VideoUrl,
    string? ImageUrl);

public sealed record UpdateExerciseRequest(
    string Name,
    string? Description,
    Muscle PrimaryMuscle,
    IReadOnlyList<Muscle>? SecondaryMuscles,
    IReadOnlyList<Equipment>? Equipment,
    string? VideoUrl,
    string? ImageUrl);

public sealed record ExerciseResponse(
    Guid Id,
    Guid? GymId,
    bool IsSystem,
    string Name,
    string? Description,
    string PrimaryMuscle,
    IReadOnlyList<string> SecondaryMuscles,
    IReadOnlyList<string> Equipment,
    string? VideoUrl,
    string? ImageUrl,
    bool IsActive,
    DateTimeOffset CreatedAt);
