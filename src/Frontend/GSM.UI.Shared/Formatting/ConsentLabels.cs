namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Textos en español de las autorizaciones del miembro (Members v2, §3.4): tipos, estados, canales y
/// los valores que el historial de la ficha guarda como <c>accepted:1:front_desk</c> / <c>revoked:1</c>.
/// </summary>
public static class ConsentLabels
{
    public const string DataProcessing = "data_processing";
    public const string LiabilityWaiver = "liability_waiver";

    public static string Type(string? type) => type switch
    {
        DataProcessing => "Autorización de tratamiento de datos",
        LiabilityWaiver => "Exoneración de responsabilidad",
        _ => type ?? string.Empty,
    };

    public static string Status(string? status) => status switch
    {
        "not_accepted" => "Sin aceptar",
        "accepted" => "Aceptada",
        "outdated" => "Pendiente de aceptar la versión vigente",
        "revoked" => "Revocada",
        _ => status ?? string.Empty,
    };

    /// <summary>¿Falta algo por aceptar? (nunca aceptada, desactualizada o revocada).</summary>
    public static bool IsPending(string? status) => status != "accepted";

    public static string Channel(string? channel) => channel switch
    {
        "app" => "App",
        "front_desk" => "Recepción (firmada en papel)",
        "import" => "Importación",
        _ => channel ?? string.Empty,
    };

    /// <summary>La versión 0 es el texto base del sistema, que el gym aún no reemplazó.</summary>
    public static string Version(int version) => version == 0 ? "texto base" : $"versión {version}";

    /// <summary>Valor de una fila <c>consent_*</c> del historial.</summary>
    public static string ChangeValue(string? value)
    {
        if (string.IsNullOrEmpty(value) || value == "none")
        {
            return "Sin aceptar";
        }

        var parts = value.Split(':');
        var version = parts.Length > 1 && int.TryParse(parts[1], out var v) ? Version(v) : string.Empty;
        return parts[0] switch
        {
            "accepted" => parts.Length > 2 ? $"Aceptada ({version}, {Channel(parts[2])})" : $"Aceptada ({version})",
            "revoked" => $"Revocada ({version})",
            _ => value,
        };
    }
}
