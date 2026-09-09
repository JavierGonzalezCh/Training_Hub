namespace GSM.Domain.Achievements;

/// <summary>Qué mide un logro. Sin <c>classes</c>: el módulo Classes es fase futura.</summary>
public enum AchievementConditionType
{
    Visits,
    Streak,

    /// <summary>Días distintos con al menos un ejercicio de rutina registrado (etapa 9.6).</summary>
    Routine_sessions,
}
