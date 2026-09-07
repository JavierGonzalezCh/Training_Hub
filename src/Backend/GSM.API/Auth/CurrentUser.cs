using System.Security.Claims;
using GSM.Application.Common.Abstractions;
using GSM.Domain.Identity;

namespace GSM.API.Auth;

/// <summary>
/// Lee la identidad de la petición desde los claims del JWT. Registrado como singleton:
/// <see cref="IHttpContextAccessor"/> resuelve el <c>HttpContext</c> de cada petición.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Principal?.FindFirstValue("sub"), out var id)
            ? id
            : null;

    public Guid? GymId =>
        Guid.TryParse(Principal?.FindFirstValue(GsmClaimTypes.GymId), out var id) ? id : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role)
            ?? Principal?.FindFirstValue("role"), ignoreCase: true, out var role)
            ? role
            : null;

    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;
}
