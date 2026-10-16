using GSM.Application.Equipment.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Equipment;

public static class EquipmentInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Equipment.</summary>
    public static IServiceCollection AddEquipmentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IEquipmentModelRepository, EquipmentModelRepository>();
        services.AddScoped<IEquipmentItemRepository, EquipmentItemRepository>();
        services.AddScoped<IEquipmentIssueRepository, EquipmentIssueRepository>();

        services.AddScoped<GSM.Application.Equipment.Public.IEquipmentReader, EquipmentReader>();

        return services;
    }
}
