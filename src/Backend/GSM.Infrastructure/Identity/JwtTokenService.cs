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

    private AccessToken Issue(IDictionary<string, object> claims, int lifetimeMinutes)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(lifetimeMinutes);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
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
