using Microsoft.Extensions.DependencyInjection;

namespace GSM.Application;

public static class AttendanceModuleRegistration
{
    /// <summary>Registro manual de la capa Application del módulo Attendance.</summary>
    public static IServiceCollection AddAttendanceModule(this IServiceCollection services)
    {
        // Candado del teclado del kiosco (A4): estado en memoria, compartido por todas las peticiones.
        services.AddSingleton<Attendance.DocumentEntryGuard>();

        // Los casos de uso (*Handler), validators y handlers de eventos se registran por assembly scanning.
        // Añade aquí solo lo que el scanning no cubra.

        return services;
    }
}
