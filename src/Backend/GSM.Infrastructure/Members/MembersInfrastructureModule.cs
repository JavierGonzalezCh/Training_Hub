using GSM.Application.Members.Abstractions;
using GSM.Application.Members.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Members;

public static class MembersInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Members.</summary>
    public static IServiceCollection AddMembersInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMemberReader, MemberReader>();

        return services;
    }
}
