namespace GSM.Application.Routines.Public;

/// <summary>Lectura de "qué me toca hoy" que Routines expone a los compositores (D2).</summary>
public interface IRoutineTodayReader
{
    /// <summary>La rutina activa del miembro resuelta a hoy. Nulo si no tiene rutina activa.</summary>
    Task<TodayTrainingView?> GetAsync(Guid memberId, CancellationToken cancellationToken = default);
}

/// <param name="DayName">Nombre del día que toca ("Pecho y tríceps") o "Descanso".</param>
/// <param name="ExerciseCount">Ejercicios del día; 0 si es descanso.</param>
/// <param name="MuscleGroups">Grupos musculares del día (nombres del enum), sin repetir.</param>
/// <param name="ReviewOn">Fecha de revisión de la rutina, solo si la armó el staff (la auto-asignada no se revisa).</param>
public sealed record TodayTrainingView(
    Guid RoutineId,
    string RoutineName,
    DateOnly Date,
    string DayName,
    bool IsRest,
    int ExerciseCount,
    IReadOnlyList<string> MuscleGroups,
    DateOnly? ReviewOn);
