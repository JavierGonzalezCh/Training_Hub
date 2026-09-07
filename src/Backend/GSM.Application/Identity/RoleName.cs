using GSM.Domain.Identity;

namespace GSM.Application.Identity;

/// <summary>Conversión canónica entre <see cref="UserRole"/> y el valor de texto del claim <c>role</c> (minúsculas).</summary>
public static class RoleName
{
    public const string SuperAdmin = "superadmin";
    public const string Owner = "owner";
    public const string Staff = "staff";
    public const string Member = "member";

    public static string ToClaim(UserRole role) => role switch
    {
        UserRole.SuperAdmin => SuperAdmin,
        UserRole.Owner => Owner,
        UserRole.Staff => Staff,
        UserRole.Member => Member,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Rol desconocido."),
    };

    public static bool TryParse(string? value, out UserRole role)
        => Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
}
