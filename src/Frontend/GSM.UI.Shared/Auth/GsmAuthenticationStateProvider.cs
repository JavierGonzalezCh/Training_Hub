using System.Security.Claims;
using GSM.Client;
using GSM.Client.Http;
using Microsoft.AspNetCore.Components.Authorization;

namespace GSM.UI.Shared.Auth;

/// <summary>
/// Estado de autenticación derivado del access token guardado. Lee <c>role</c>, <c>gym_id</c> e
/// <c>impersonating</c> del JWT (sin validar: la validación real la hace la API). Un token caducado
/// se trata como anónimo; el <see cref="AuthenticatedHttpHandler"/> se encarga del refresh cuando
/// una petición devuelve 401.
/// </summary>
public sealed class GsmAuthenticationStateProvider(ITokenStore tokenStore) : AuthenticationStateProvider
{
    public const string AuthenticationType = "gsm-jwt";

    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var tokens = await tokenStore.GetAsync();
        var payload = JwtPayload.TryRead(tokens?.AccessToken);

        if (payload is null || payload.IsExpired(DateTimeOffset.UtcNow))
        {
            return Anonymous;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, payload.Subject ?? string.Empty),
            new(ClaimTypes.Name, payload.Name ?? string.Empty),
        };

        if (!string.IsNullOrEmpty(payload.Role))
        {
            claims.Add(new Claim(ClaimTypes.Role, payload.Role));
        }

        if (payload.GymId is { } gymId)
        {
            claims.Add(new Claim("gym_id", gymId.ToString()));
        }

        if (payload.Impersonating)
        {
            claims.Add(new Claim("impersonating", "true"));
        }

        var identity = new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>Reevalúa el estado tras un login, un logout o un cambio de tokens (impersonación).</summary>
    public void NotifyChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}

public static class GsmClaimsPrincipalExtensions
{
    public static bool IsImpersonating(this ClaimsPrincipal user)
        => user.HasClaim("impersonating", "true");

    public static Guid? GymId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst("gym_id")?.Value, out var id) ? id : null;

    public static string? Role(this ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Role)?.Value;
}
