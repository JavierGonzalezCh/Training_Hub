using System.Security.Claims;
using System.Text;
using GSM.Application.Identity;
using GSM.Domain.Identity;
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

        var authorization = services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.RequireSuperAdmin, p => p.RequireRole(RoleName.SuperAdmin))
            .AddPolicy(AuthPolicies.RequireOwner, p => p.RequireRole(RoleName.Owner))
            .AddPolicy(AuthPolicies.RequireStaff, p => p.RequireAssertion(ctx =>
                ctx.User.IsInRole(RoleName.Owner) || ctx.User.IsInRole(RoleName.Staff) || IsImpersonating(ctx.User)))
            .AddPolicy(AuthPolicies.RequireStaffOrSuperAdmin, p => p.RequireRole(RoleName.SuperAdmin, RoleName.Owner, RoleName.Staff))
            .AddPolicy(AuthPolicies.RequireMember, p => p.RequireRole(RoleName.Member));

        // Una política por permiso especial de staff (Identity §5): `[RequirePermission(...)]`.
        foreach (var permission in StaffPermissions.All)
        {
            authorization.AddPolicy(
                AuthPolicies.ForPermission(permission),
                p => p.RequireAssertion(ctx => PermissionRules.Has(ctx.User, permission)));
        }

        return services;
    }

    /// <summary>
    /// Superadmin con token de impersonación: entra en los endpoints del gym visitado como si fuera
    /// staff, porque su token lleva el rol real (<c>superadmin</c>) y sin esto recibiría 403 en todo el
    /// gym. Solo lectura: <c>SubscriptionGateFilter</c> corta cualquier método distinto de GET con ese
    /// token, así que abrir la política no abre escrituras. <c>RequireOwner</c> no se abre: no protege
    /// ningún GET.
    /// </summary>
    private static bool IsImpersonating(ClaimsPrincipal user)
        => user.IsInRole(RoleName.SuperAdmin)
           && string.Equals(user.FindFirst(GsmClaims.Impersonating)?.Value, "true", StringComparison.OrdinalIgnoreCase);
}

public static class AuthPolicies
{
    public const string RequireSuperAdmin = "RequireSuperAdmin";
    public const string RequireOwner = "RequireOwner";
    public const string RequireStaff = "RequireStaff";
    public const string RequireStaffOrSuperAdmin = "RequireStaffOrSuperAdmin";
    public const string RequireMember = "RequireMember";

    /// <summary>Política que exige un permiso especial de staff (<see cref="RequirePermissionAttribute"/>).</summary>
    public static string ForPermission(StaffPermission permission) => "Permission:" + PermissionName.ToClaim(permission);
}
