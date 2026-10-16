using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Textos de los reportes de falla de máquinas (Equipment E4), compartidos por el panel y la app del miembro.
/// Los motivos viajan al servidor por su valor numérico (el cliente generado los declara <c>int</c> en el cuerpo)
/// y vuelven como texto (<c>NotWorking</c>, <c>Unsafe</c>…).
/// </summary>
public static class EquipmentIssueLabels
{
    public const string ReasonOther = "Other";
    public const string ReasonUnsafe = "Unsafe";
    public const int OtherValue = 6;
    public const int UnsafeValue = 5;

    /// <summary>Motivos en el orden del enum del servidor: valor, nombre en la API, etiqueta.</summary>
    public static readonly (int Value, string Name, string Label)[] Reasons =
    [
        (0, "NotWorking", "No funciona"),
        (1, "DamagedPart", "Pieza dañada"),
        (2, "StrangeNoise", "Ruido raro"),
        (3, "MissingPart", "Falta una pieza"),
        (4, "Dirty", "Sucia"),
        (5, "Unsafe", "Insegura"),
        (6, "Other", "Otro"),
    ];

    /// <summary>Estados del reporte: valor del enum, nombre en la API, etiqueta.</summary>
    public static readonly (int Value, string Name, string Label)[] Statuses =
    [
        (0, "Open", "Abierto"),
        (1, "InProgress", "En revisión"),
        (2, "Resolved", "Resuelto"),
        (3, "Dismissed", "Descartado"),
    ];

    public static string Reason(string name)
        => Reasons.FirstOrDefault(r => r.Name == name).Label ?? name;

    public static string Status(string name)
        => Statuses.FirstOrDefault(s => s.Name == name).Label ?? name;

    public static bool IsUnclosed(string status) => status is "Open" or "InProgress";

    public static Color StatusColor(string status) => status switch
    {
        "Open" => Color.Warning,
        "InProgress" => Color.Info,
        "Resolved" => Color.Success,
        _ => Color.Default,
    };
}
