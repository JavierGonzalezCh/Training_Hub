namespace GSM.UI.Shared.Auth;

/// <summary>
/// Destino al que volver tras iniciar sesión (<c>/login?returnUrl=...</c>). Solo se acepta una
/// ruta local: cualquier otra cosa se ignora, para que el login no sirva de redirección abierta
/// hacia un sitio externo.
/// </summary>
public static class ReturnUrl
{
    /// <summary>La ruta local limpia, o <c>null</c> si el valor no es una ruta local segura.</summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        // "/ruta" sí; "//otro-sitio", "/\otro-sitio", "https://..." y "javascript:..." no.
        if (value[0] != '/' || value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\'))
        {
            return null;
        }

        return value.Any(char.IsControl) ? null : value;
    }

    /// <summary>URL del login que, al terminar, devuelve a <paramref name="baseRelativePath"/> (ruta + query, sin "/" inicial).</summary>
    public static string LoginFor(string baseRelativePath)
    {
        var target = Sanitize("/" + baseRelativePath.TrimStart('/'));

        // El área del miembro (/app/...) tiene su propia pantalla de entrada, con la marca del gym y el código
        // por correo; el resto entra por el login del panel.
        var login = IsMemberArea(target) ? "/app/entrar" : "/login";
        return target is null or "/" ? login : $"{login}?returnUrl={Uri.EscapeDataString(target)}";
    }

    private static bool IsMemberArea(string? path)
        => path is not null
           && (path.Equals("/app", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/app/", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/app?", StringComparison.OrdinalIgnoreCase));
}
