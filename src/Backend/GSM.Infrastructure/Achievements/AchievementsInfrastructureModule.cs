using GSM.Application.Achievements.Abstractions;
using GSM.Application.Achievements.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Achievements;

public static class AchievementsInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Achievements.</summary>
    public static IServiceCollection AddAchievementsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAchievementRepository, AchievementRepository>();
        services.AddScoped<IRankingRepository, RankingRepository>();
        services.AddScoped<IAchievementReader, AchievementReader>();
        services.AddScoped<IAchievementService, AchievementService>();
        services.AddScoped<IRetroactiveAchievementGrant, RetroactiveAchievementGrant>();

        return services;
    }
}
