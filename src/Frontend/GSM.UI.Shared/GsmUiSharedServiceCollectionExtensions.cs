using GSM.Client;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Theming;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.UI.Shared;

public static class GsmUiSharedServiceCollectionExtensions
{
    /// <summary>Registra el estado de sesión compartido por los tres hosts (panel, admin, socio).</summary>
    public static IServiceCollection AddGsmUiShared(this IServiceCollection services)
    {
        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(GsmPolicies.RequireStaff, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole(GsmRoles.Owner) || ctx.User.IsInRole(GsmRoles.Staff)
                    || (ctx.User.IsInRole(GsmRoles.SuperAdmin) && ctx.User.IsImpersonating())));
            options.AddPolicy(GsmPolicies.RequireStaffOrSuperAdmin, policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole(GsmRoles.Owner) || ctx.User.IsInRole(GsmRoles.Staff) || ctx.User.IsInRole(GsmRoles.SuperAdmin)));
        });
        services.AddScoped<GsmAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        services.AddScoped<GymContext>();
        services.AddScoped<SessionService>();
        services.AddScoped<ImpersonationService>();
        return services;
    }
}
