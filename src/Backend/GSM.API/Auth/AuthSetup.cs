using System.Text;
using GSM.Application.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GSM.API.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddGsmAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Falta la sección de configuración 'Jwt'.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = GsmClaims.Role,
                    NameClaimType = GsmClaims.Name,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.RequireSuperAdmin, p => p.RequireRole(RoleName.SuperAdmin))
            .AddPolicy(AuthPolicies.RequireOwner, p => p.RequireRole(RoleName.Owner))
            .AddPolicy(AuthPolicies.RequireStaff, p => p.RequireRole(RoleName.Owner, RoleName.Staff))
            .AddPolicy(AuthPolicies.RequireStaffOrSuperAdmin, p => p.RequireRole(RoleName.SuperAdmin, RoleName.Owner, RoleName.Staff))
            .AddPolicy(AuthPolicies.RequireMember, p => p.RequireRole(RoleName.Member));

        return services;
    }
}

public static class AuthPolicies
{
    public const string RequireSuperAdmin = "RequireSuperAdmin";
    public const string RequireOwner = "RequireOwner";
    public const string RequireStaff = "RequireStaff";
    public const string RequireStaffOrSuperAdmin = "RequireStaffOrSuperAdmin";
    public const string RequireMember = "RequireMember";
}
