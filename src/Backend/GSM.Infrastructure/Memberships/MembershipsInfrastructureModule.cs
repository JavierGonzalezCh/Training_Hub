using GSM.Application.Memberships.Abstractions;
using GSM.Application.Memberships.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Memberships;

public static class MembershipsInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Memberships.</summary>
    public static IServiceCollection AddMembershipsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IMembershipReader, MembershipReader>();

        return services;
    }
}
