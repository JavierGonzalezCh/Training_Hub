namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Cómo se nombra el negocio en los textos del panel: «tu gimnasio», «tu box», «tu estudio», «tu negocio», «tu academia»
/// (docs/GSM-tipos-de-negocio.md §10). Solo cambia la palabra; «miembro» y la marca TrainingHub no cambian en ningún tipo.
/// Se usa donde el texto queda raro con «gimnasio» (onboarding, avisos), no en masa.
/// </summary>
public static class BusinessNoun
{
    /// <summary>La palabra suelta: <c>gimnasio</c>, <c>box</c>, <c>estudio</c>, <c>negocio</c>, <c>academia</c>.</summary>
    public static string Bare(string? gymType) => (gymType ?? string.Empty).ToLowerInvariant() switch
    {
        "box" => "box",
        "studio" => "estudio",
        "academy" => "academia",
        "personaltrainer" or "personal_trainer" or "other" => "negocio",
        _ => "gimnasio",
    };

    /// <summary>Con posesivo: <c>tu gimnasio</c>, <c>tu box</c>…</summary>
    public static string Your(string? gymType) => $"tu {Bare(gymType)}";

    /// <summary>Con la inicial en mayúscula: <c>Tu estudio</c>.</summary>
    public static string YourCapitalized(string? gymType)
    {
        var text = Your(gymType);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
