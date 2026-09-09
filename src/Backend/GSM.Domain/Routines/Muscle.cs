namespace GSM.Domain.Routines;

/// <summary>
/// Grupos musculares del catálogo de ejercicios. Conjunto cerrado y estable (anatomía, no marcas).
/// Los miembros con guion bajo existen para que <c>LowerCaseNameTranslator</c> produzca la label
/// nativa de Postgres exacta (<c>lower_back</c>, <c>full_body</c>).
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
    Cardio,
}
