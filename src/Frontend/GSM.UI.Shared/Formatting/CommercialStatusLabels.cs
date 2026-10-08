using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Estado comercial del miembro (Memberships v2, §10). Llega de la API en snake_case:
/// <c>no_plan</c>, <c>current</c>, <c>expiring</c>, <c>grace</c>, <c>expired</c>, <c>frozen</c>.
/// </summary>
public static class CommercialStatusLabels
{
    /// <summary>Valores del filtro de la lista, en el orden en que se muestran.</summary>
    public static readonly (string Value, string Label)[] Filters =
    [
        ("current", "Al día"),
        ("expiring", "Por vencer"),
        ("grace", "En gracia"),
        ("expired", "Vencido"),
        ("frozen", "Congelado"),
        ("no_plan", "Sin plan"),
    ];

    public static string Label(string? status) => status switch
    {
        "current" => "Al día",
        "expiring" => "Por vencer",
        "grace" => "En gracia",
        "expired" => "Vencido",
        "frozen" => "Congelado",
        "no_plan" => "Sin plan",
        _ => status ?? "—",
    };

    public static Color Color(string? status) => status switch
    {
        "current" => MudBlazor.Color.Info,
        "expiring" or "grace" => MudBlazor.Color.Warning,
        "expired" => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default,
    };
}
