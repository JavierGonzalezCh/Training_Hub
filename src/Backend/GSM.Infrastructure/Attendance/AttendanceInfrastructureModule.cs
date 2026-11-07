using GSM.Application.Attendance.Abstractions;
using GSM.Application.Attendance.Public;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure.Attendance;

public static class AttendanceInfrastructureRegistration
{
    /// <summary>Registro de la capa Infrastructure del módulo Attendance.</summary>
    public static IServiceCollection AddAttendanceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IAttendanceReader, AttendanceReader>();
        services.AddScoped<GSM.Application.Routines.Abstractions.IRemoteTrainingPolicy, RemoteTrainingPolicy>();
        services.AddScoped<IMemberEntryRuleRepository, MemberEntryRuleRepository>();
        services.AddScoped<ICheckinSettingsRepository, CheckinSettingsRepository>();
        services.AddScoped<ICheckinAttemptRepository, CheckinAttemptRepository>();
        services.AddSingleton<ICheckinCodeSigner, HmacCheckinCodeSigner>();
        services.AddOptions<CheckinOptions>()
            .Bind(configuration.GetSection(CheckinOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
