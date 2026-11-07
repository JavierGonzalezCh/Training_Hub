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

    /// <summary>Días con entrada antes de las 8:00 (hora del gym). Label: <c>early_visits</c>.</summary>
    EarlyVisits,

    /// <summary>Días con entrada desde las 20:00 (hora del gym). Label: <c>late_visits</c>.</summary>
    LateVisits,

    /// <summary>Fines de semana distintos con entrada el sábado o el domingo. Label: <c>weekends</c>.</summary>
    Weekends,

    /// <summary>Días en que registró todos los ejercicios de un día de su rutina. Label: <c>complete_sessions</c>.</summary>
    CompleteSessions,

    /// <summary>Clases distintas a las que asistió (Spinning, Yoga...). Label: <c>distinct_classes</c>.</summary>
    DistinctClasses,

    /// <summary>Cuántos de los tres levantamientos (sentadilla, press de banca, peso muerto) registró. Label: <c>power_lifts</c>.</summary>
    PowerLifts,

    /// <summary>
    /// Terminar un mes cerrado del ranking en el puesto <c>ConditionValue</c> o mejor (50, 10, 3, 1). Label:
    /// <c>monthly_ranking_top</c>.
    /// </summary>
    MonthlyRankingTop,
}
