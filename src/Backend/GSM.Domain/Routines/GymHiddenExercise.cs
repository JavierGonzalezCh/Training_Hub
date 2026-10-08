using GSM.Domain.Common;

namespace GSM.Domain.Routines;

/// <summary>
/// Un ejercicio del sistema que un gym oculta: deja de salir en el buscador y en el editor, pero las
/// rutinas que ya lo usan no cambian. Solo aplica a ejercicios del sistema.
/// </summary>
public sealed class GymHiddenExercise : BaseEntity, ITenantEntity
{
    private GymHiddenExercise()
    {
    }

    public Guid GymId { get; private set; }

    public Guid ExerciseId { get; private set; }

    public static GymHiddenExercise Create(Guid gymId, Guid exerciseId)
        => new() { GymId = gymId, ExerciseId = exerciseId };
}
