using System.Text;
using GSM.Domain.Identity;

namespace GSM.Application.Identity;

/// <summary>
/// Texto canónico (snake_case) de los permisos y perfiles de staff: el que viaja en el claim
/// <c>perm</c> del token y en los DTOs de la API.
/// </summary>
public static class PermissionName
{
    public static string ToClaim(StaffPermission permission) => Snake(permission.ToString());

    public static bool TryParse(string? value, out StaffPermission permission)
    {
        foreach (var candidate in StaffPermissions.All)
        {
            if (string.Equals(ToClaim(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                permission = candidate;
                return true;
            }
        }

        permission = default;
        return false;
    }

    public static string ToProfileName(StaffProfile profile) => Snake(profile.ToString());

    public static bool TryParseProfile(string? value, out StaffProfile profile)
    {
        foreach (var candidate in Enum.GetValues<StaffProfile>())
        {
            if (string.Equals(ToProfileName(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                profile = candidate;
                return true;
            }
        }

        profile = default;
        return false;
    }

    private static string Snake(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }
}
