namespace GSM.Domain.Routines;

/// <summary>Tipo de ejercicio: filtro del catálogo y base del <see cref="TrackingType"/> sugerido.</summary>
public enum ExerciseType
{
    Strength,
    Cardio,
    Mobility,
    Stretching,
    Plyometrics,
}

/// <summary>
/// Qué se mide en cada serie. Lo heredan las líneas de las rutinas y los registros. Mapea a la label
/// nativa de Postgres con <c>SnakeCaseNameTranslator</c> (<c>reps_weight</c>, <c>time_distance</c>).
/// </summary>
public enum TrackingType
{
    RepsWeight,
    Reps,
    Time,
    Distance,
    TimeDistance,
}

/// <summary>Nivel de dificultad del ejercicio.</summary>
public enum Difficulty
{
    Beginner,
    Intermediate,
    Advanced,
}

/// <summary>Zona del cuerpo. Se deriva del músculo principal (<see cref="MuscleGroups.Of"/>); no se guarda.</summary>
public enum MuscleGroup
{
    UpperBody,
    LowerBody,
    Core,
    FullBody,
}

public static class MuscleGroups
{
    /// <summary>Mapa único <c>Muscle → MuscleGroup</c>, igual para todos los gyms. Lumbar va en core.</summary>
    public static MuscleGroup Of(Muscle muscle) => muscle switch
    {
        Muscle.Chest or Muscle.Back or Muscle.Shoulders or Muscle.Traps or Muscle.Biceps
            or Muscle.Triceps or Muscle.Forearms or Muscle.Neck => MuscleGroup.UpperBody,
        Muscle.Quadriceps or Muscle.Hamstrings or Muscle.Glutes or Muscle.Calves
            or Muscle.Adductors or Muscle.Abductors => MuscleGroup.LowerBody,
        Muscle.Abs or Muscle.Obliques or Muscle.Lower_back => MuscleGroup.Core,
        Muscle.Full_body => MuscleGroup.FullBody,
        _ => throw new ArgumentOutOfRangeException(nameof(muscle), muscle, null),
    };

    /// <summary>Los músculos de un grupo (para el filtro en dos pasos).</summary>
    public static IReadOnlyList<Muscle> MusclesOf(MuscleGroup group)
        => Enum.GetValues<Muscle>().Where(m => Of(m) == group).ToList();
}

public static class ExerciseDefaults
{
    /// <summary>Qué se mide por defecto según el tipo; el usuario puede cambiarlo.</summary>
    public static TrackingType SuggestedTracking(ExerciseType type) => type switch
    {
        ExerciseType.Cardio => TrackingType.TimeDistance,
        ExerciseType.Mobility or ExerciseType.Stretching => TrackingType.Time,
        _ => TrackingType.RepsWeight,
    };
}
