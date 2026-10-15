using System.Security.Claims;
using System.Text;
using GSM.Application.Identity;
using GSM.Domain.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
                options.Events = SecurityEventLogging.JwtEvents();
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

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, LoggingAuthorizationResultHandler>();

        var authorization = services.AddAuthorizationBuilder()
            // `[Authorize]` a secas = cualquier sesión de persona. El token de un kiosco (Identity §8) solo
            // abre los endpoints que lo nombran (`RequireKiosk`): sin esto pasaría por todos los
            // controllers que no declaran política.
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => !ctx.User.IsInRole(RoleName.Kiosk))
                .Build())
            // A3: el kiosco es solo del dispositivo emparejado. Ni el staff ni el Owner (ni soporte
            // impersonando) lo abren con su sesión: el equipo de la entrada no lleva el panel.
            .AddPolicy(AuthPolicies.RequireKiosk, p => p.RequireRole(RoleName.Kiosk))
            .AddPolicy(AuthPolicies.RequireSuperAdmin, p => p.RequireRole(RoleName.SuperAdmin))
            .AddPolicy(AuthPolicies.RequireOwner, p => p.RequireAssertion(ctx =>
                ctx.User.IsInRole(RoleName.Owner) || IsImpersonating(ctx.User)))
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
    /// token en modo lectura, así que abrir la política no abre escrituras; en modo escritura (S6) sí
    /// pasan, con permiso para todo. <c>RequireOwner</c> se abre igual: soporte tiene permiso para todo
    /// (Owners, staff, ajustes del gym) y, en modo lectura, el filtro sigue cortando todo lo que no sea GET.
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
    /// <summary>Solo la cuenta de dispositivo del kiosco (Identity §8, Attendance §6): lo que el kiosco necesita, hoy el código del QR.</summary>
    public const string RequireKiosk = "RequireKiosk";
    public const string RequireStaffOrSuperAdmin ="RequireStaffOrSuperAdmin";
    public const string RequireMember = "RequireMember";

    /// <summary>Política que exige un permiso especial de staff (<see cref="RequirePermissionAttribute"/>).</summary>
    public static string ForPermission(StaffPermission permission) => "Permission:" + PermissionName.ToClaim(permission);
}
