namespace GSM.Domain.Identity;

/// <summary>
/// Permisos especiales de un acceso de staff (<c>docs/GSM-modulo-identity.md</c> §5.1). Todo lo demás
/// que hace el staff (registrar miembros, cobrar, registrar entradas, armar rutinas, manejar clases)
/// está abierto a cualquier staff y no es un permiso. El Owner los tiene todos siempre.
/// </summary>
public enum StaffPermission
{
    /// <summary>Ver y editar datos de salud (Members).</summary>
    ViewMemberHealth,

    /// <summary>Ser entrenador asignado de miembros (Routines).</summary>
    Trainer,

    /// <summary>Anular entradas (Attendance).</summary>
    ManageAttendance,

    /// <summary>Anular pagos y aprobar solicitudes de anulación (Payments).</summary>
    VoidPayments,

    /// <summary>Ver ingresos, cierre diario y cajas de otros (Payments).</summary>
    ViewFinances,
}

/// <summary>Perfiles predefinidos de staff (§5.2). Fijos del sistema: cada gym no crea los suyos.</summary>
public enum StaffProfile
{
    /// <summary>Ninguno de los permisos especiales.</summary>
    Reception,

    /// <summary><see cref="StaffPermission.Trainer"/> y <see cref="StaffPermission.ViewMemberHealth"/>.</summary>
    Trainer,

    /// <summary>Salud, asistencia, anulación de pagos y finanzas.</summary>
    Administrator,
}

/// <summary>Lo que trae cada perfil y cómo se calculan los permisos efectivos de un acceso.</summary>
public static class StaffPermissions
{
    public static readonly IReadOnlyList<StaffPermission> All = Enum.GetValues<StaffPermission>();

    /// <summary>Permisos que trae el perfil, sin ajustes.</summary>
    public static IReadOnlySet<StaffPermission> ForProfile(StaffProfile profile) => profile switch
    {
        StaffProfile.Reception => new HashSet<StaffPermission>(),
        StaffProfile.Trainer => new HashSet<StaffPermission> { StaffPermission.Trainer, StaffPermission.ViewMemberHealth },
        StaffProfile.Administrator => new HashSet<StaffPermission>
        {
            StaffPermission.ViewMemberHealth,
            StaffPermission.ManageAttendance,
            StaffPermission.VoidPayments,
            StaffPermission.ViewFinances,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Perfil desconocido."),
    };

    /// <summary>Permisos efectivos = los del perfil + los agregados − los quitados.</summary>
    public static IReadOnlySet<StaffPermission> Effective(
        StaffProfile profile, IEnumerable<(StaffPermission Permission, bool Granted)> adjustments)
    {
        var result = new HashSet<StaffPermission>(ForProfile(profile));
        foreach (var (permission, granted) in adjustments)
        {
            if (granted)
            {
                result.Add(permission);
            }
            else
            {
                result.Remove(permission);
            }
        }

        return result;
    }
}
