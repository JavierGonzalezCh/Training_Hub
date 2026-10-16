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

    /// <summary>
    /// Lo otorga el staff a mano (L4): sin condición automática, nunca se evalúa ni se otorga de forma
    /// retroactiva. Label: <c>manual</c>.
    /// </summary>
    Manual,

    /// <summary>
    /// Días con un récord personal batido en ejercicios de repeticiones con peso (L3), solo de rutinas asignadas.
    /// Label: <c>personal_records</c>.
    /// </summary>
    PersonalRecords,

    /// <summary>
    /// Volumen total levantado en kg: suma de peso × repeticiones de las series (sin calentamiento) de ejercicios
    /// de repeticiones con peso, solo de rutinas asignadas (L3). Label: <c>lifted_volume</c>.
    /// </summary>
    LiftedVolume,
}
