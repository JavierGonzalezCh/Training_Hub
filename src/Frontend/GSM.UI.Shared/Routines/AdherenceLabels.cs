namespace GSM.UI.Shared.Routines;

/// <summary>Textos y colores del cumplimiento de la rutina (docs/GSM-tipos-de-negocio.md §6).</summary>
public static class AdherenceLabels
{
    /// <summary>Por debajo de este porcentaje el miembro «va atrasado» (mismo corte que el filtro del servidor).</summary>
    public const int BehindThresholdPercent = 50;

    public static string StatusLabel(string status) => status switch
    {
        "done" => "Hecho",
        "missed" => "Falto",
        "today" => "Hoy",
        "upcoming" => "Pendiente",
        "rest" => "Descanso",
        "offplan" => "Fuera de plan",
        _ => "—",
    };

    public static string StatusCssClass(string status) => $"gsm-adh-day gsm-adh-day--{status}";

    /// <summary>«2 de 4 días».</summary>
    public static string DaysOf(int done, int planned) => planned == 1 ? $"{done} de 1 día" : $"{done} de {planned} días";

    public static string Percent(int? percent) => percent is { } p ? $"{p} %" : "—";

    public static bool IsBehind(int? percent) => percent is { } p && p < BehindThresholdPercent;

    /// <summary>
    /// F6: franja de color de una semana cerrada: <c>low</c> (rojo, menos de 50 %), <c>mid</c> (amarillo, 50–79 %) y
    /// <c>high</c> (verde, 80 % o más). Sin porcentaje, <c>none</c>.
    /// </summary>
    public static string Band(int? percent) => percent switch
    {
        null => "none",
        < BehindThresholdPercent => "low",
        < 80 => "mid",
        _ => "high",
    };
}
