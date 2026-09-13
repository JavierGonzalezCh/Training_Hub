using GSM.UI.Shared.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.UI.Shared;

public static class GsmUiSharedServiceCollectionExtensions
{
    /// <summary>Registra el estado de sesión compartido por los tres hosts (panel, admin, socio).</summary>
    public static IServiceCollection AddGsmUiShared(this IServiceCollection services)
    {
        services.AddAuthorizationCore();
        services.AddScoped<GsmAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<GsmAuthenticationStateProvider>());
        services.AddScoped<GymContext>();
        services.AddScoped<SessionService>();
        return services;
    }
}
