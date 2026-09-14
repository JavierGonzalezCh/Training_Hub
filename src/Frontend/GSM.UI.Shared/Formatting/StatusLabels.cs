using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Traducción de los estados que llegan como string desde la API (nombre del miembro C# del
/// enum, ver <c>CLAUDE.md</c>) a etiqueta en español y color de marca: cian = activo/positivo,
/// rojo = vencido/urgente, plata = neutro.
/// </summary>
public static class StatusLabels
{
    public static string Label(string status) => status switch
    {
        "Active" => "Activo",
        "Inactive" => "Inactivo",
        "Expired" => "Vencido",
        "Pending" => "Pendiente",
        "Cancelled" => "Cancelado",
        "Booked" => "Reservado",
        "Attended" => "Asistió",
        "NoShow" => "No asistió",
        _ => status,
    };

    public static Color Color(string status) => status switch
    {
        "Active" or "Attended" or "Booked" => MudBlazor.Color.Info,
        "Expired" or "Cancelled" or "NoShow" => MudBlazor.Color.Error,
        "Inactive" or "Pending" => MudBlazor.Color.Default,
        _ => MudBlazor.Color.Default,
    };
}
