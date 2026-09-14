using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>Estado efectivo/manual del gym y plan comercial, en español (ver CLAUDE.md, notas de SuperAdmin).</summary>
public static class SuperAdminLabels
{
    public static readonly (int Value, string Label)[] Plans = [(0, "Basic"), (1, "Pro"), (2, "Enterprise")];

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
        "Active" or "Trial" => Color.Info,
        "Overdue" => Color.Warning,
        "Disabled" or "Suspended" => Color.Error,
        _ => Color.Default,
    };
}
