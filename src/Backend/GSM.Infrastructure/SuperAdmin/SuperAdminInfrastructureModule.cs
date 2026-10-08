using GSM.Application.SuperAdmin.Abstractions;
using GSM.Application.SuperAdmin.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.SuperAdmin;

public static class SuperAdminInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo SuperAdmin.</summary>
    public static IServiceCollection AddSuperAdminInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IGymSubscriptionRepository, GymSubscriptionRepository>();
        services.AddScoped<ISubscriptionPaymentRepository, SubscriptionPaymentRepository>();
        services.AddScoped<IImpersonationSessionRepository, ImpersonationSessionRepository>();
        services.AddSingleton<IImpersonationActionRecorder, ImpersonationActionRecorder>();
        services.AddScoped<ISubscriptionReader, SubscriptionReader>();
        services.AddScoped<IMonthlyMetricRepository, MonthlyMetricRepository>();
        services.AddScoped<GSM.Application.Identity.Abstractions.IGymLoginGate, SubscriptionLoginGate>();

        return services;
    }
}
