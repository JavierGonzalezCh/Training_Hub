using GSM.Member.UI.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Member.UI;

public static class MemberUiServiceCollectionExtensions
{
    public static IServiceCollection AddGsmMemberUi(this IServiceCollection services)
    {
        services.AddScoped<MemberContext>();
        services.AddScoped<MemberBootstrapService>();
        services.AddScoped<NewsUnreadState>();
        return services;
    }
}
