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
    /// Nº de sesiones de clase a las que el miembro asistió (etapa 10.4). Label: <c>classes</c>.
    /// </summary>
    Classes,

    /// <summary>
    /// Meses como miembro: días cubiertos por membresías (sin canceladas ni días congelados) entre 30 (L2).
    /// Label: <c>tenure_months</c>.
    /// </summary>
    TenureMonths,

    /// <summary>Amigos referidos que ya tienen su primera membresía pagada (L2). Label: <c>referrals</c>.</summary>
    Referrals,
}
