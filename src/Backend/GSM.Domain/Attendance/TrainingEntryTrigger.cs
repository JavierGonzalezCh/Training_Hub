namespace GSM.Domain.Attendance;

/// <summary>
/// Cuándo cuenta el día de un miembro cuando el gym no registra la entrada en la puerta (docs/GSM-tipos-de-negocio.md §5.2).
/// Label en Postgres: <c>start</c> / <c>first_log</c>.
/// </summary>
public enum TrainingEntryTrigger
{
    /// <summary>El día cuenta al tocar «Iniciar rutina» o «Registrar entrenamiento» (lo de siempre).</summary>
    Start,

    /// <summary>El día cuenta al registrar el primer ejercicio del día; tocar un botón ya no basta.</summary>
    FirstLog,
}
