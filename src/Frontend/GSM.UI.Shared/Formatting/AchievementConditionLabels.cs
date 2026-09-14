namespace GSM.UI.Shared.Formatting;

/// <summary>
/// <c>AchievementConditionType</c> del backend (<c>Visits=0, Streak=1, RoutineSessions=2, Classes=3</c>).
/// </summary>
public static class AchievementConditionLabels
{
    public static readonly (int Value, string Label)[] All =
    [
        (0, "Visitas"),
        (1, "Racha (días seguidos)"),
        (2, "Sesiones de rutina"),
        (3, "Clases"),
    ];

    /// <summary>Nombre del enum (<c>"Visits"</c>…) → valor entero para el <c>&lt;select&gt;</c> de edición.</summary>
    public static int ToValue(string conditionType) => conditionType switch
    {
        "Visits" => 0,
        "Streak" => 1,
        "RoutineSessions" => 2,
        "Classes" => 3,
        _ => 0,
    };

    public static string Label(string conditionType) => conditionType switch
    {
        "Visits" => "Visitas",
        "Streak" => "Racha (días seguidos)",
        "RoutineSessions" => "Sesiones de rutina",
        "Classes" => "Clases",
        _ => conditionType,
    };
}
