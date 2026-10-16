namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Duración de un plan por tiempo (Memberships v2, §3). La unidad llega de la API como
/// <c>days</c> o <c>months</c>.
/// </summary>
public static class PlanDurationLabels
{
    public const string Days = "days";

    public const string Months = "months";

    /// <summary>Opciones del selector de unidad del formulario de planes.</summary>
    public static readonly (string Value, string Label)[] Units =
    [
        (Days, "Días"),
        (Months, "Meses del calendario"),
    ];

    public const string KindTime = "time";

    public const string KindPass = "pass";

    /// <summary>Opciones del selector de tipo de plan (MS5).</summary>
    public static readonly (string Value, string Label)[] Kinds =
    [
        (KindTime, "Por tiempo"),
        (KindPass, "Tiquetera (N entradas)"),
    ];

    public static bool IsPass(string? kind) => string.Equals(kind, KindPass, StringComparison.OrdinalIgnoreCase);

    /// <summary>"12 entradas · 30 días" para una tiquetera; para un plan por tiempo, su duración.</summary>
    public static string FormatPlan(string? kind, string? unit, int value, int? entries)
        => IsPass(kind)
            ? $"{Entries(entries ?? 0)} · {(value == 1 ? "1 día" : $"{value} días")}"
            : Format(unit, value);

    /// <summary>Un plan con más de un cupo es de grupo (MS7).</summary>
    public static bool IsGroup(int seats) => seats > 1;

    /// <summary>"Grupo de 4" para un plan de grupo; vacío para un plan individual.</summary>
    public static string SeatsLabel(int seats) => seats > 1 ? $"Grupo de {seats}" : string.Empty;

    /// <summary>"1 entrada", "12 entradas".</summary>
    public static string Entries(int count) => count == 1 ? "1 entrada" : $"{count} entradas";

    /// <summary>"Quedan 4 de 12 entradas" (tiquetera). Con 1 restante: "Queda 1 de 12 entradas".</summary>
    public static string Balance(int left, int total)
        => $"{(left == 1 ? "Queda" : "Quedan")} {left} de {total} entradas";

    /// <summary>"1 mes", "3 meses", "1 día", "30 días". Sin unidad se asume días (planes de antes).</summary>
    public static string Format(string? unit, int value)
        => string.Equals(unit, Months, StringComparison.OrdinalIgnoreCase)
            ? value == 1 ? "1 mes" : $"{value} meses"
            : value == 1 ? "1 día" : $"{value} días";
}
