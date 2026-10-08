using GSM.Application.Gyms.Abstractions;
using GSM.Application.Gyms.Public;
using GSM.Domain.Gyms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Gyms;

public static class GymsInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Gyms.</summary>
    public static IServiceCollection AddGymsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IGymRepository, GymRepository>();
        services.AddScoped<IGymReader, GymReader>();
        services.AddScoped<IGymAdministration, GymAdministration>();

        return services;
    }
}
