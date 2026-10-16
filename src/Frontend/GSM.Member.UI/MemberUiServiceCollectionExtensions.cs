using GSM.Member.UI.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GSM.Member.UI;

public static class MemberUiServiceCollectionExtensions
{
    public static IServiceCollection AddGsmMemberUi(this IServiceCollection services)
    {
        services.AddScoped<MemberContext>();
        services.AddScoped<MemberBootstrapService>();
        services.AddScoped<NewsUnreadState>();
        services.AddScoped<MemberLoginBranding>();
        // A6: sin implementación de plataforma, la ubicación no está disponible (la web y MAUI la reemplazan).
        services.TryAddScoped<IDeviceLocation, UnsupportedDeviceLocation>();
        return services;
    }
}
