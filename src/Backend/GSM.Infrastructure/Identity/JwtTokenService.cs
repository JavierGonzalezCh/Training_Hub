using System.Text;
using GSM.Application.Identity;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GSM.Infrastructure.Identity;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Impersonación: vida corta fija, independiente de <c>AccessTokenMinutes</c>.</summary>
    private const int ImpersonationTokenMinutes = 15;

    public AccessToken CreateAccessToken(AuthenticatedUser user)
    {
        var claims = new Dictionary<string, object>
        {
            [GsmClaims.Subject] = user.Id.ToString(),
            [GsmClaims.Role] = RoleName.ToClaim(user.Role),
            [GsmClaims.Name] = user.FullName,
        };

        if (user.GymId is { } gymId)
        {
            claims[GsmClaims.GymId] = gymId.ToString();
        }

        // Un claim por permiso (arreglo JSON en el token; el middleware lo expande a varios claims).
        if (user.Permissions is { Count: > 0 } permissions)
        {
            claims[GsmClaims.Permission] = permissions.Select(PermissionName.ToClaim).ToArray();
        }

        return Issue(claims, _options.AccessTokenMinutes);
    }

    public AccessToken CreateImpersonationToken(ImpersonationTokenRequest request)
    {
        var claims = new Dictionary<string, object>
        {
            [GsmClaims.Subject] = request.SuperAdminUserId.ToString(),
            [GsmClaims.Role] = RoleName.ToClaim(UserRole.SuperAdmin),
            [GsmClaims.Name] = request.SuperAdminName,
            [GsmClaims.GymId] = request.GymId.ToString(),
            [GsmClaims.Impersonating] = "true",
            [GsmClaims.ImpersonationSession] = request.SessionId.ToString(),
        };

        return Issue(claims, ImpersonationTokenMinutes);
    }

    /// <summary>Boleto de selección de gym: vida corta y audiencia propia (no sirve como access token).</summary>
    private const int SelectionTicketMinutes = 5;

    private const string SelectionTicketType = "gym_selection";

    private string SelectionAudience => _options.Audience + ".gym-selection";

    public AccessToken CreateGymSelectionTicket(Guid userId)
        => Issue(
            new Dictionary<string, object>
            {
                [GsmClaims.Subject] = userId.ToString(),
                ["typ_ticket"] = SelectionTicketType,
            },
            SelectionTicketMinutes,
            SelectionAudience);

    public async Task<Guid?> ValidateGymSelectionTicketAsync(string ticket)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = SelectionAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(ticket, parameters);
        if (!result.IsValid
            || !result.Claims.TryGetValue("typ_ticket", out var type)
            || !string.Equals(type as string, SelectionTicketType, StringComparison.Ordinal)
            || !result.Claims.TryGetValue(GsmClaims.Subject, out var sub)
            || !Guid.TryParse(sub as string, out var userId))
        {
            return null;
        }

        return userId;
    }

    private AccessToken Issue(IDictionary<string, object> claims, int lifetimeMinutes, string? audience = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(lifetimeMinutes);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = audience ?? _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
        };

        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return new AccessToken(handler.CreateToken(descriptor), expiresAt);
    }
}
