using System.Security.Claims;
using GSM.Application.Identity;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace GSM.API.Auth;

/// <summary>
/// Regla única de "¿tiene este permiso especial de staff?" sobre los claims del token. La comparten
/// <see cref="CurrentUser.HasPermission"/> y las políticas de autorización.
/// </summary>
public static class PermissionRules
{
    public static bool Has(ClaimsPrincipal user, StaffPermission permission)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // El Owner tiene todos siempre; el SuperAdmin impersonando también (S6).
        if (user.IsInRole(RoleName.Owner) || IsImpersonating(user))
        {
            return true;
        }

        // Solo el staff los lleva en el token: un miembro nunca trae claims `perm`.
        return user.IsInRole(RoleName.Staff)
               && user.HasClaim(GsmClaims.Permission, PermissionName.ToClaim(permission));
    }

    public static bool IsImpersonating(ClaimsPrincipal user)
        => user.IsInRole(RoleName.SuperAdmin)
           && string.Equals(user.FindFirst(GsmClaims.Impersonating)?.Value, "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Exige un permiso especial de staff en un controller o acción:
/// <c>[RequirePermission(StaffPermission.ViewFinances)]</c>. El Owner y el SuperAdmin impersonando
/// lo cumplen siempre; un staff, si su perfil (más ajustes) lo incluye. Responde 403 si no.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(StaffPermission permission)
    : AuthorizeAttribute(AuthPolicies.ForPermission(permission))
{
    public StaffPermission Permission { get; } = permission;
}
