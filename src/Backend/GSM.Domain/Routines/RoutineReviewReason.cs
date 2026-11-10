namespace GSM.Domain.Routines;

/// <summary>
/// Por qué una rutina está (o estará) en «Rutinas por revisar». El entrenador distingue entre revisar la rutina completa
/// porque el miembro la armó o la cambió, y la revisión periódica por plazo cumplido.
/// </summary>
public enum RoutineReviewReason
{
    /// <summary>Se cumplió el plazo de revisión del gym.</summary>
    Scheduled,

    /// <summary>La armó el propio miembro y nadie del staff la ha visto.</summary>
    CreatedByMember,

    /// <summary>El miembro la cambió después de crearla (o de su última revisión).</summary>
    EditedByMember,
}
