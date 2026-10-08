namespace GSM.Client;

/// <summary>
/// Valores del claim <c>perm</c> (permisos especiales de staff) tal como los emite la API. Espejo de
/// <c>PermissionName</c> en el backend. El Owner y un SuperAdmin impersonando los tienen todos.
/// </summary>
public static class GsmPermissions
{
    public const string ViewMemberHealth = "view_member_health";
    public const string Trainer = "trainer";
    public const string ManageAttendance = "manage_attendance";
    public const string VoidPayments = "void_payments";
    public const string ViewFinances = "view_finances";

    public static readonly IReadOnlyList<string> All =
        [ViewMemberHealth, Trainer, ManageAttendance, VoidPayments, ViewFinances];
}
