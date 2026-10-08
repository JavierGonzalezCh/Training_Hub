namespace GSM.Application.Identity;

/// <summary>Nombres de los claims que viajan en el access token de GSM.</summary>
public static class GsmClaims
{
    public const string Subject = "sub";
    public const string GymId = "gym_id";
    public const string Role = "role";
    public const string Name = "name";

    /// <summary>
    /// Permiso especial de staff (<see cref="PermissionName"/>); el claim aparece una vez por permiso.
    /// El token de un Owner los lleva todos.
    /// </summary>
    public const string Permission = "perm";

    /// <summary>Presente y "true" solo en tokens de impersonación (soporte de solo lectura).</summary>
    public const string Impersonating = "impersonating";

    /// <summary>Id de la sesión de auditoría de impersonación.</summary>
    public const string ImpersonationSession = "impersonation_session";
}
