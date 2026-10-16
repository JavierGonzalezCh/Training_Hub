namespace GSM.UI.Shared.Formatting;

/// <summary>
/// <c>AchievementConditionType</c> del backend (<c>Visits=0, Streak=1, RoutineSessions=2, Classes=3, TenureMonths=4, Referrals=5, Manual=6</c>).
/// </summary>
public static class AchievementConditionLabels
{
    public static readonly (int Value, string Label)[] All =
    [
        (0, "Visitas"),
        (1, "Racha (días seguidos)"),
        (2, "Sesiones de rutina"),
        (3, "Clases"),
        (4, "Antigüedad (meses)"),
        (5, "Amigos referidos"),
        (6, "Otorgado por el staff"),
    ];

    /// <summary>Qué cuenta el valor de cada condición, para el rótulo del campo del formulario.</summary>
    public static string ValueLabel(int value) => value switch
    {
        0 => "Valor (nº de visitas)",
        1 => "Valor (días seguidos)",
        2 => "Valor (nº de sesiones)",
        3 => "Valor (nº de clases)",
        4 => "Valor (meses con membresía)",
        5 => "Valor (amigos que se inscribieron)",
        6 => "Sin condición: lo otorga el staff",
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
        _ => 0,
    };

    /// <summary>Valor entero del tipo manual: no tiene condición numérica.</summary>
    public const int Manual = 6;

    public static string Label(string conditionType) => conditionType switch
    {
        "Visits" => "Visitas",
        "Streak" => "Racha (días seguidos)",
        "RoutineSessions" => "Sesiones de rutina",
        "Classes" => "Clases",
        "TenureMonths" => "Antigüedad (meses)",
        "Referrals" => "Amigos referidos",
        "Manual" => "Otorgado por el staff",
        _ => conditionType,
    };
}
