using System.Security.Claims;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity;
using GSM.Domain.Identity;

namespace GSM.API.Auth;

/// <summary>
/// Lee la identidad de la petición desde los claims del JWT. Registrado como singleton:
/// <see cref="IHttpContextAccessor"/> resuelve el <c>HttpContext</c> de cada petición.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(GsmClaims.Subject), out var id) ? id : null;

    public Guid? GymId =>
        Guid.TryParse(Principal?.FindFirstValue(GsmClaims.GymId), out var id) ? id : null;

    public UserRole? Role =>
        RoleName.TryParse(Principal?.FindFirstValue(GsmClaims.Role), out var role) ? role : null;

    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;

    public bool IsImpersonating =>
        string.Equals(Principal?.FindFirstValue(GsmClaims.Impersonating), "true", StringComparison.OrdinalIgnoreCase);

    public Guid? ImpersonationSessionId =>
        Guid.TryParse(Principal?.FindFirstValue(GsmClaims.ImpersonationSession), out var id) ? id : null;

    public bool HasPermission(StaffPermission permission)
        => Principal is { } principal && PermissionRules.Has(principal, permission);
}
