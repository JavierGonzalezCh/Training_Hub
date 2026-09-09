namespace GSM.Domain.Achievements;

/// <summary>Qué mide un logro. Labels en Postgres vía el traductor snake_case de Infrastructure.</summary>
public enum AchievementConditionType
{
    Visits,
    Streak,

    /// <summary>
    /// Días distintos con al menos un ejercicio de rutina registrado (etapa 9.6).
    /// Label en Postgres: <c>routine_sessions</c>.
    /// </summary>
    RoutineSessions,

    /// <summary>
    /// Nº de sesiones de clase a las que el socio asistió (etapa 10.4). Label: <c>classes</c>.
    /// </summary>
    Classes,
}
