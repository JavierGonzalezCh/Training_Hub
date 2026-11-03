using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class MembersModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Members.</summary>
    public static IServiceCollection AddMembersModule(this IServiceCollection services)
    {
        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        services.AddScoped<GSM.Application.Members.MemberChangeRecorder>();
        services.AddScoped<GSM.Application.Members.ConsentService>();
        services.AddScoped<GSM.Application.Members.MyProfileComposer>();
        services.AddScoped<GSM.Application.Members.MemberAnonymizer>();

        // FL1: puerto de Identity para el login por celular y el primer ingreso con el documento.
        services.AddScoped<GSM.Application.Identity.Abstractions.IMemberLoginDirectory, GSM.Application.Members.MemberLoginDirectory>();

        return services;
    }
}
