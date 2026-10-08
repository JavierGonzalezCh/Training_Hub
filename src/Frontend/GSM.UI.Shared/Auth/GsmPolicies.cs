namespace GSM.UI.Shared.Auth;

/// <summary>Nombres de política, espejo de <c>AuthPolicies</c> en la API.</summary>
public static class GsmPolicies
{
    /// <summary>Owner, Staff, o un SuperAdmin impersonando (mismo criterio que <c>RequireStaff</c> del backend).</summary>
    public const string RequireStaff = "RequireStaff";

    /// <summary>Owner, Staff o SuperAdmin (con o sin impersonar) — para el catálogo de ejercicios,
    /// donde el superadmin edita los del sistema (mismo criterio que <c>RequireStaffOrSuperAdmin</c>).</summary>
    public const string RequireStaffOrSuperAdmin = "RequireStaffOrSuperAdmin";

    /// <summary>
    /// Política que exige un permiso especial de staff (<see cref="GSM.Client.GsmPermissions"/>), para
    /// <c>AuthorizeView Policy="..."</c>. Espejo de <c>AuthPolicies.ForPermission</c>.
    /// </summary>
    public static string ForPermission(string permission) => "Permission:" + permission;
}
