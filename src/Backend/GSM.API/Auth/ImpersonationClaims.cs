using System.Security.Claims;
using GSM.Application.Identity;
using GSM.Domain.Identity;

namespace GSM.API.Auth;

/// <summary>Lectura del modo de impersonación en los claims del token (S6).</summary>
public static class ImpersonationClaims
{
    /// <summary>
    /// <c>true</c> si el token es de un SuperAdmin impersonando en modo <c>write</c>. Un token sin el
    /// claim de modo (anterior a S6) cuenta como lectura.
    /// </summary>
    public static bool CanWrite(ClaimsPrincipal? user)
        => user is not null
           && PermissionRules.IsImpersonating(user)
           && string.Equals(
               user.FindFirst(GsmClaims.ImpersonationMode)?.Value,
               GsmClaims.ImpersonationModeWrite,
               StringComparison.OrdinalIgnoreCase);
}
