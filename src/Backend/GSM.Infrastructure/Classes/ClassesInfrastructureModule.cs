using GSM.Application.Classes.Abstractions;
using GSM.Application.Classes.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Classes;

public static class ClassesInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Classes.</summary>
    public static IServiceCollection AddClassesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IClassSessionRepository, ClassSessionRepository>();
        services.AddScoped<IClassBookingRepository, ClassBookingRepository>();
        services.AddScoped<IClassSettingsRepository, ClassSettingsRepository>();
        services.AddScoped<IBookingBlockRepository, BookingBlockRepository>();
        services.AddScoped<IWaitlistRepository, WaitlistRepository>();
        services.AddScoped<IClassReader, ClassReader>();

        return services;
    }
}
