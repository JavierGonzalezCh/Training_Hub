using GSM.Client.Generated;
using GSM.Client.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Client;

public static class GsmClientServiceCollectionExtensions
{
    /// <summary>
    /// Registra el cliente tipado de la API. El host debe registrar antes su <see cref="ITokenStore"/>
    /// (<c>localStorage</c> en WASM, <c>SecureStorage</c> en MAUI).
    /// </summary>
    public static IServiceCollection AddGsmClient(this IServiceCollection services, string apiBaseUrl)
    {
        var baseUri = new Uri(apiBaseUrl, UriKind.Absolute);

        services.AddSingleton(new ApiClientOptions { ApiBaseUrl = apiBaseUrl });
        services.AddTransient<AuthenticatedHttpHandler>();

        // Refresh: cliente desnudo, sin el handler autenticado, para no entrar en bucle en el 401.
        services.AddHttpClient<ITokenRefresher, HttpTokenRefresher>(c => c.BaseAddress = baseUri);

        AddApiClient<IAchievementsClient, AchievementsClient>(services, baseUri);
        AddApiClient<IAdminDashboardClient, AdminDashboardClient>(services, baseUri);
        AddApiClient<IAdminGymsClient, AdminGymsClient>(services, baseUri);
        AddApiClient<IAdminGymOwnersClient, AdminGymOwnersClient>(services, baseUri);
        AddApiClient<IAdminImpersonationClient, AdminImpersonationClient>(services, baseUri);
        AddApiClient<IAdminLogsClient, AdminLogsClient>(services, baseUri);
        AddApiClient<IAdminMetricsClient, AdminMetricsClient>(services, baseUri);
        AddApiClient<IAdminPaymentsClient, AdminPaymentsClient>(services, baseUri);
        AddApiClient<IAdminPlansClient, AdminPlansClient>(services, baseUri);
        AddApiClient<IAdminCatalogClient, AdminCatalogClient>(services, baseUri);
        AddApiClient<IAttendanceClient, AttendanceClient>(services, baseUri);
        AddApiClient<IAnnouncementsClient, AnnouncementsClient>(services, baseUri);
        AddApiClient<IWhatsAppClient, WhatsAppClient>(services, baseUri);
        AddApiClient<IAuthClient, AuthClient>(services, baseUri);
        AddApiClient<ICashRegisterClient, CashRegisterClient>(services, baseUri);
        AddApiClient<ICashShiftsClient, CashShiftsClient>(services, baseUri);
        AddApiClient<IClassesClient, ClassesClient>(services, baseUri);
        AddApiClient<IClassSettingsClient, ClassSettingsClient>(services, baseUri);
        AddApiClient<IClassBookingBlocksClient, ClassBookingBlocksClient>(services, baseUri);
        AddApiClient<IMeClassBookingStatusClient, MeClassBookingStatusClient>(services, baseUri);
        AddApiClient<IDashboardClient, DashboardClient>(services, baseUri);
        AddApiClient<IEquipmentClient, EquipmentClient>(services, baseUri);
        AddApiClient<IMemberEquipmentClient, MemberEquipmentClient>(services, baseUri);
        AddApiClient<IMemberWaitlistClient, MemberWaitlistClient>(services, baseUri);
        AddApiClient<IExercisesClient, ExercisesClient>(services, baseUri);
        AddApiClient<IGymClient, GymClient>(services, baseUri);
        AddApiClient<IGymsClient, GymsClient>(services, baseUri);
        AddApiClient<IHealthClient, HealthClient>(services, baseUri);
        AddApiClient<IMeClient, MeClient>(services, baseUri);
        AddApiClient<IMeProfileClient, MeProfileClient>(services, baseUri);
        AddApiClient<IMemberAttendanceClient, MemberAttendanceClient>(services, baseUri);
        AddApiClient<IMemberClassesClient, MemberClassesClient>(services, baseUri);
        AddApiClient<IMemberClassAttendanceClient, MemberClassAttendanceClient>(services, baseUri);
        AddApiClient<IMemberMembershipsClient, MemberMembershipsClient>(services, baseUri);
        AddApiClient<IMemberPaymentsClient, MemberPaymentsClient>(services, baseUri);
        AddApiClient<IMembersClient, MembersClient>(services, baseUri);
        AddApiClient<IMemberSettingsClient, MemberSettingsClient>(services, baseUri);
        AddApiClient<IConsentDocumentsClient, ConsentDocumentsClient>(services, baseUri);
        AddApiClient<IMemberConsentsClient, MemberConsentsClient>(services, baseUri);
        AddApiClient<IMeConsentsClient, MeConsentsClient>(services, baseUri);
        AddApiClient<IMembershipPlansClient, MembershipPlansClient>(services, baseUri);
        AddApiClient<IMembershipsClient, MembershipsClient>(services, baseUri);
        AddApiClient<IMembershipSettingsClient, MembershipSettingsClient>(services, baseUri);
        AddApiClient<IMembershipGroupsClient, MembershipGroupsClient>(services, baseUri);
        AddApiClient<IPublicCommunicationsClient, PublicCommunicationsClient>(services, baseUri);
        AddApiClient<IPaymentsClient, PaymentsClient>(services, baseUri);
        AddApiClient<IRoutineExercisesClient, RoutineExercisesClient>(services, baseUri);
        AddApiClient<IRoutinesClient, RoutinesClient>(services, baseUri);
        AddApiClient<IMemberRoutinesClient, MemberRoutinesClient>(services, baseUri);
        AddApiClient<IMemberAdherenceClient, MemberAdherenceClient>(services, baseUri);
        AddApiClient<IRoutineTemplatesClient, RoutineTemplatesClient>(services, baseUri);
        AddApiClient<IRoutineSettingsClient, RoutineSettingsClient>(services, baseUri);
        AddApiClient<IStaffClient, StaffClient>(services, baseUri);
        AddApiClient<IOwnersClient, OwnersClient>(services, baseUri);
        AddApiClient<IKiosksClient, KiosksClient>(services, baseUri);
        AddApiClient<IStaffNotificationsClient, StaffNotificationsClient>(services, baseUri);
        AddApiClient<INotificationSettingsClient, NotificationSettingsClient>(services, baseUri);
        AddApiClient<IPushClient, PushClient>(services, baseUri);
        AddApiClient<IPublicGymsClient, PublicGymsClient>(services, baseUri);
        AddApiClient<ILogsClient, LogsClient>(services, baseUri);

        return services;
    }

    private static void AddApiClient<TClient, TImplementation>(IServiceCollection services, Uri baseUri)
        where TClient : class
        where TImplementation : class, TClient
    {
        services
            .AddHttpClient<TClient, TImplementation>(c => c.BaseAddress = baseUri)
            .AddHttpMessageHandler<AuthenticatedHttpHandler>();
    }
}
