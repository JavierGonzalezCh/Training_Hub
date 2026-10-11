using FluentValidation;
using GSM.Application.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra por assembly scanning todo lo que aporta la capa Application:
    /// casos de uso (clases <c>*Handler</c>), validators de FluentValidation y
    /// handlers de eventos de dominio.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        services.AddScoped<GSM.Application.Common.Logs.LoggedJobRunner>();

        var eventHandlerInterface = typeof(IDomainEventHandler<>);

        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false })
            {
                continue;
            }

            var closedEventHandlers = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == eventHandlerInterface)
                .ToArray();

            if (closedEventHandlers.Length > 0)
            {
                foreach (var handlerInterface in closedEventHandlers)
                {
                    services.AddScoped(handlerInterface, type);
                }

                continue;
            }

            // Convención: cada caso de uso es una clase que termina en "Handler",
            // se inyecta directo en el controller (sin bus de comandos).
            if (type.Name.EndsWith("Handler", StringComparison.Ordinal))
            {
                services.AddScoped(type);
            }
        }

        return services
            .AddIdentityModule()
            .AddGymsModule()
            .AddMembersModule()
            .AddMembershipsModule()
            .AddPaymentsModule()
            .AddAttendanceModule()
            .AddAchievementsModule()
            .AddRoutinesModule()
            .AddClassesModule()
            .AddEquipmentModule()
            .AddSuperAdminModule()
            .AddCommunicationsModule()
            .AddNotificationsModule();
    }
}
