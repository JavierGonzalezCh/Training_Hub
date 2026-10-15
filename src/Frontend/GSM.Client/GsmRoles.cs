namespace GSM.Client;

/// <summary>Valores del claim <c>role</c> tal como los emite la API (minúsculas). Espejo de <c>RoleName</c>.</summary>
public static class GsmRoles
{
    public const string SuperAdmin = "superadmin";
    public const string Owner = "owner";
    public const string Staff = "staff";
    public const string Member = "member";

    /// <summary>Cuenta de dispositivo del kiosco (Identity §8). No es una persona: solo abre <c>/kiosk</c>.</summary>
    public const string Kiosk = "kiosk";

    /// <summary>Área de la SPA a la que pertenece cada rol tras el login.</summary>
    public static string HomeFor(string? role) => role switch
    {
        SuperAdmin => "/admin",
        Member => "/app",
        Kiosk => "/kiosk",
        Owner or Staff => "/",
        _ => "/login",
    };
}
