using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Routines;

public static class RoutinesInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Routines.</summary>
    public static IServiceCollection AddRoutinesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<IExerciseEquipmentRepository, ExerciseEquipmentRepository>();
        services.AddScoped<IExerciseAlternativeRepository, ExerciseAlternativeRepository>();
        services.AddScoped<IRoutineRepository, RoutineRepository>();
        services.AddScoped<IRoutineTemplateRepository, RoutineTemplateRepository>();
        services.AddScoped<IRoutineLogRepository, RoutineLogRepository>();
        services.AddScoped<IRoutineReader, RoutineReader>();
        services.AddScoped<IAdherenceSource, AdherenceSource>();

        return services;
    }
}
