using GSM.Application.Payments.Abstractions;
using GSM.Application.Payments.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Payments;

public static class PaymentsInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Payments.</summary>
    public static IServiceCollection AddPaymentsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentReader, PaymentReader>();
        services.AddScoped<IVoidRequestRepository, VoidRequestRepository>();

        return services;
    }
}
