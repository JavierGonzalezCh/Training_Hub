namespace GSM.Domain.Routines;

/// <summary>
/// Músculos del catálogo de ejercicios. Conjunto cerrado y estable (anatomía, no marcas).
/// Los miembros con guion bajo existen para que <c>LowerCaseNameTranslator</c> produzca la label
/// nativa de Postgres exacta (<c>lower_back</c>, <c>full_body</c>). El grupo muscular se deriva
/// de aquí (<see cref="MuscleGroups.Of"/>), nunca se guarda. <c>cardio</c> dejó de ser un músculo
/// en la v2: ahora es <see cref="ExerciseType.Cardio"/>. Los valores nuevos van al final para no
/// mover el ordinal de los existentes que el cliente envía como entero.
/// </summary>
public enum Muscle
{
    Chest,
    Back,
    Shoulders,
    Biceps,
    Triceps,
    Forearms,
    Quadriceps,
    Hamstrings,
    Glutes,
    Calves,
    Abs,
    Lower_back,
    Neck,
    Full_body,
    Traps,
    Obliques,
    Adductors,
    Abductors,
}
