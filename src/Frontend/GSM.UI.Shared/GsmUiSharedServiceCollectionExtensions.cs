using GSM.Client;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Theming;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GSM.UI.Shared;

public static class GsmUiSharedServiceCollectionExtensions
{
    /// <summary>Registra el estado de sesión compartido por los tres hosts (panel, admin, miembro).</summary>
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

            // Una política por permiso especial de staff (Identity §5): para preguntar desde las pantallas.
            foreach (var permission in GsmPermissions.All)
            {
                options.AddPolicy(GsmPolicies.ForPermission(permission), policy =>
                    policy.RequireAssertion(ctx => ctx.User.HasPermission(permission)));
            }
        });
        services.AddScoped<GsmAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        services.AddScoped<GymContext>();
        services.AddScoped<SessionService>();
        services.AddScoped<ImpersonationService>();
        services.AddScoped<GSM.UI.Shared.Components.ViewportState>();

        // Push (N2): cada host registra su IPushNotifications (la web, el navegador); sin uno, no hay push.
        services.TryAddScoped<GSM.UI.Shared.Push.IPushNotifications, GSM.UI.Shared.Push.UnsupportedPushNotifications>();
        services.AddScoped<GSM.UI.Shared.Push.PushRegistrationService>();

        // Bloqueo del modo staff (AP5): cada host nativo registra su IDeviceLock, IAppLifecycle e ISecureValueStore.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<GSM.UI.Shared.Security.IDeviceLock, GSM.UI.Shared.Security.NullDeviceLock>();
        services.TryAddSingleton<GSM.UI.Shared.Security.IAppLifecycle, GSM.UI.Shared.Security.NullAppLifecycle>();
        services.TryAddSingleton<GSM.UI.Shared.Security.StaffLockState>();
        services.TryAddSingleton<GSM.UI.Shared.Security.ISecureValueStore, GSM.UI.Shared.Security.InMemorySecureValueStore>();
        services.TryAddSingleton<GSM.UI.Shared.Security.DevicePinService>();

        // Errores del cliente (LG3): cada host registra su IClientEnvironment; el reporte alimenta logs.client_errors.
        services.AddScoped<GSM.UI.Shared.Diagnostics.ClientErrorReporter>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(
            sp => new GSM.UI.Shared.Diagnostics.ClientErrorLoggerProvider(sp));
        return services;
    }
}
