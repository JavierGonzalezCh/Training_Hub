using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>Estado efectivo/manual del gym y plan comercial, en español (ver CLAUDE.md, notas de SuperAdmin).</summary>
public static class SuperAdminLabels
{
    public static readonly (int Value, string Label)[] Plans = [(0, "Basic"), (1, "Pro"), (2, "Enterprise")];

    /// <summary>Uso del límite de miembros: "142 / 150", o "142 / sin límite" si el plan no lo tiene.</summary>
    public static string MemberUsage(int counted, int? limit)
        => limit is { } max ? $"{counted} / {max}" : $"{counted} / sin límite";

    /// <summary>Proporción 0–1 del límite usada, o <c>null</c> sin límite.</summary>
    public static double? MemberUsageRatio(int counted, int? limit)
        => limit is { } max and > 0 ? Math.Clamp((double)counted / max, 0, 1) : null;

    /// <summary>Valor del enum heredado de plan que acompaña al nombre (0 si el plan es uno nuevo).</summary>
    public static int LegacyPlanValue(string planName)
        => Plans.FirstOrDefault(p => string.Equals(p.Label, planName, StringComparison.OrdinalIgnoreCase)).Value;

    public static string StatusLabel(string status) => status switch
    {
        "Trial" => "Prueba",
        "Active" => "Activo",
        "Overdue" => "Moroso",
        "Disabled" => "Deshabilitado",
        "Suspended" => "Suspendido",
        _ => status,
    };

    public static Color StatusColor(string status) => status switch
    {
        // "Activo" es un estado correcto, no informativo: verde. "Prueba" no pide ninguna
        // acción, así que se queda neutro en vez de competir con los estados que sí.
        "Active" => Color.Success,
        "Trial" => Color.Default,
        "Overdue" => Color.Warning,
        "Disabled" or "Suspended" => Color.Error,
        _ => Color.Default,
    };
}
