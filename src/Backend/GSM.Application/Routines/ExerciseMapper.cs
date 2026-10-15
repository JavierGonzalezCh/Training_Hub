using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;

namespace GSM.Application.Routines;

/// <summary>
/// Mapeo manual: <c>IsHidden</c> no sale de la entidad sino de <c>routines.gym_hidden_exercises</c> para el gym
/// actual, y <c>MuscleGroup</c> se deriva del músculo principal.
/// </summary>
public static class ExerciseMapper
{
    public static ExerciseResponse ToResponse(
        Exercise exercise, bool isHidden = false, ExerciseEquipmentInfo? availability = null)
        => new(
            exercise.Id,
            exercise.GymId,
            exercise.IsSystem,
            exercise.Name,
            exercise.Description,
            exercise.PrimaryMuscle.ToString(),
            exercise.MuscleGroup.ToString(),
            exercise.SecondaryMuscles.Select(m => m.ToString()).ToList(),
            exercise.Equipment.Select(e => e.ToString()).ToList(),
            exercise.ExerciseType.ToString(),
            exercise.TrackingType.ToString(),
            exercise.IsUnilateral,
            exercise.Difficulty.ToString(),
            exercise.VideoUrl,
            exercise.ImageUrl,
            exercise.IsActive,
            isHidden,
            exercise.CreatedAt,
            availability?.IsAvailable ?? true,
            availability?.Machines ?? []);
}
