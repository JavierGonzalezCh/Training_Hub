namespace GSM.Client.Generated;

// El cliente generado por NSwag, en su plantilla por defecto, descarta el cuerpo crudo de la
// respuesta cuando la deserialización tiene éxito (ReadResponseAsString = false optimiza para
// el camino feliz). GsmApiException.Response queda vacío en ese caso, así que ApiError no puede
// leer el detail/title de un 409/422/403. Cada cliente expone Initialize() como partial method
// justo para esto: forzamos a que siempre retenga el texto.
public partial class AchievementsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AdminDashboardClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AdminGymsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AdminGymOwnersClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AdminImpersonationClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AdminPaymentsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AttendanceClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AuthClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class CashRegisterClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ClassesClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class DashboardClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ExercisesClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class GymClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class GymsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class HealthClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MeClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MemberAttendanceClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MemberClassesClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MemberMembershipsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MemberPaymentsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MembersClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MembershipPlansClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MembershipsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class PaymentsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class RoutineExercisesClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class RoutinesClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MemberRoutinesClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class StaffClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class AnnouncementsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class PublicCommunicationsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class StaffNotificationsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MembershipSettingsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class EquipmentClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class WhatsAppClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class ConsentDocumentsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MemberConsentsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class MeConsentsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}

public partial class NotificationSettingsClient
{
    partial void Initialize() => ReadResponseAsString = true;
}
