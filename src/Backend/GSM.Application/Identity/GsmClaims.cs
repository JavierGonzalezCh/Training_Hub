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

    /// <summary>Modo de la impersonación: <c>read</c> (solo GET) o <c>write</c> (todos los métodos).</summary>
    public const string ImpersonationMode = "impersonation_mode";

    public const string ImpersonationModeWrite = "write";

    /// <summary>
    /// FL1: presente y "true" mientras la cuenta deba crear su contraseña (entró con la cédula). La API solo
    /// atiende las acciones marcadas con <c>[AllowWhilePasswordChangeRequired]</c>.
    /// </summary>
    public const string PasswordChangeRequired = "pwd_change";
}
