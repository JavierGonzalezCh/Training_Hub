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

    /// <summary>"1 mes", "3 meses", "1 día", "30 días". Sin unidad se asume días (planes de antes).</summary>
    public static string Format(string? unit, int value)
        => string.Equals(unit, Months, StringComparison.OrdinalIgnoreCase)
            ? value == 1 ? "1 mes" : $"{value} meses"
            : value == 1 ? "1 día" : $"{value} días";
}
