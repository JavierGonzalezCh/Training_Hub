namespace GSM.UI.Shared.Formatting;

/// <summary>
/// <c>AchievementConditionType</c> del backend (<c>Visits=0, Streak=1, RoutineSessions=2, Classes=3, TenureMonths=4,
/// Referrals=5, Manual=6, PersonalRecords=7, LiftedVolume=8, EarlyVisits=9, LateVisits=10, Weekends=11,
/// CompleteSessions=12, DistinctClasses=13, PowerLifts=14, MonthlyRankingTop=15</c>).
/// </summary>
public static class AchievementConditionLabels
{
    public static readonly (int Value, string Label)[] All =
    [
        (0, "Visitas"),
        (1, "Racha (días seguidos)"),
        (12, "Sesiones de rutina completas"),
        (2, "Días con rutina registrada"),
        (3, "Clases"),
        (13, "Clases distintas"),
        (7, "Récords personales"),
        (8, "Volumen levantado (kg)"),
        (14, "Sentadilla, banca y peso muerto"),
        (9, "Entradas antes de las 8:00"),
        (10, "Entradas desde las 20:00"),
        (11, "Fines de semana"),
        (15, "Puesto en el ranking del mes"),
        (4, "Antigüedad (meses)"),
        (5, "Amigos referidos"),
        (6, "Otorgado por el staff"),
    ];

    /// <summary>Qué cuenta el valor de cada condición, para el rótulo del campo del formulario.</summary>
    public static string ValueLabel(int value) => value switch
    {
        0 => "Valor (nº de visitas)",
        1 => "Valor (días seguidos)",
        2 => "Valor (nº de días con rutina)",
        3 => "Valor (nº de clases)",
        4 => "Valor (meses con membresía)",
        5 => "Valor (amigos que se inscribieron)",
        6 => "Sin condición: lo otorga el staff",
        7 => "Valor (récords personales batidos)",
        8 => "Valor (kilos levantados en total)",
        9 => "Valor (días entrenando antes de las 8:00)",
        10 => "Valor (días entrenando desde las 20:00)",
        11 => "Valor (fines de semana con entrada)",
        12 => "Valor (sesiones con todos los ejercicios)",
        13 => "Valor (clases distintas)",
        14 => "Valor (de 1 a 3 levantamientos)",
        15 => "Valor (puesto: 50, 10, 3 o 1)",
        _ => "Valor",
    };

    /// <summary>Nombre del enum (<c>"Visits"</c>…) → valor entero para el <c>&lt;select&gt;</c> de edición.</summary>
    public static int ToValue(string conditionType) => conditionType switch
    {
        "Visits" => 0,
        "Streak" => 1,
        "RoutineSessions" => 2,
        "Classes" => 3,
        "TenureMonths" => 4,
        "Referrals" => 5,
        "Manual" => 6,
        "PersonalRecords" => 7,
        "LiftedVolume" => 8,
        "EarlyVisits" => 9,
        "LateVisits" => 10,
        "Weekends" => 11,
        "CompleteSessions" => 12,
        "DistinctClasses" => 13,
        "PowerLifts" => 14,
        "MonthlyRankingTop" => 15,
        _ => 0,
    };

    /// <summary>Valor entero del tipo manual: no tiene condición numérica.</summary>
    public const int Manual = 6;

    /// <summary>Valor entero del puesto en el ranking: la meta es un puesto, no un conteo.</summary>
    public const int MonthlyRankingTop = 15;

    public static string Label(string conditionType) => conditionType switch
    {
        "Visits" => "Visitas",
        "Streak" => "Racha (días seguidos)",
        "RoutineSessions" => "Días con rutina registrada",
        "Classes" => "Clases",
        "TenureMonths" => "Antigüedad (meses)",
        "Referrals" => "Amigos referidos",
        "Manual" => "Otorgado por el staff",
        "PersonalRecords" => "Récords personales",
        "LiftedVolume" => "Volumen levantado (kg)",
        "EarlyVisits" => "Entradas antes de las 8:00",
        "LateVisits" => "Entradas desde las 20:00",
        "Weekends" => "Fines de semana",
        "CompleteSessions" => "Sesiones completas",
        "DistinctClasses" => "Clases distintas",
        "PowerLifts" => "Sentadilla, banca y peso muerto",
        "MonthlyRankingTop" => "Puesto en el ranking del mes",
        _ => conditionType,
    };

    /// <summary>Lo que se muestra como avance: «7/10», o para el ranking «Top 10».</summary>
    public static string Progress(string conditionType, int progress, int value)
        => conditionType == "MonthlyRankingTop"
            ? value == 1 ? "Primer puesto en un mes" : $"Top {value} en un mes"
            : $"{progress}/{value}";
}
