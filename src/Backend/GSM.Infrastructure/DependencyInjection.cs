using GSM.Infrastructure.Achievements;
using GSM.Infrastructure.Attendance;
using GSM.Infrastructure.Classes;
using GSM.Infrastructure.Common;
using GSM.Infrastructure.Communications;
using GSM.Infrastructure.Gyms;
using GSM.Infrastructure.Identity;
using GSM.Infrastructure.Members;
using GSM.Infrastructure.Memberships;
using GSM.Infrastructure.Payments;
using GSM.Infrastructure.Routines;
using GSM.Infrastructure.SuperAdmin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Compone la capa Infrastructure: lo transversal y un método por módulo
    /// (<c>Add{Módulo}Infrastructure</c>, en la carpeta de cada módulo).
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services
            .AddCommonInfrastructure(configuration)
            .AddIdentityInfrastructure(configuration)
            .AddGymsInfrastructure(configuration)
            .AddMembersInfrastructure(configuration)
            .AddMembershipsInfrastructure(configuration)
            .AddPaymentsInfrastructure(configuration)
            .AddAttendanceInfrastructure(configuration)
            .AddCommunicationsInfrastructure(configuration)
            .AddAchievementsInfrastructure(configuration)
            .AddRoutinesInfrastructure(configuration)
            .AddClassesInfrastructure(configuration)
            .AddSuperAdminInfrastructure(configuration);
    }
}
