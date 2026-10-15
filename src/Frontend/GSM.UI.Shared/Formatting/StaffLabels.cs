using GSM.Client;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Textos en español de los perfiles y permisos de staff (que la API entrega como snake_case:
/// <c>view_member_health</c>, <c>administrator</c>…) y de las filas de auditoría.
/// </summary>
public static class StaffLabels
{
    public static readonly (string Value, string Label)[] Profiles =
    [
        ("reception", "Recepción"),
        ("trainer", "Entrenador"),
        ("administrator", "Administrador"),
    ];

    public static readonly (string Value, string Label, string Help)[] Permissions =
    [
        (GsmPermissions.ViewMemberHealth, "Ver y editar datos de salud", "Ficha de salud de los miembros."),
        (GsmPermissions.Trainer, "Ser entrenador de miembros", "Puede quedar como entrenador asignado."),
        (GsmPermissions.ManageAttendance, "Anular entradas", "Corregir el registro de asistencia."),
        (GsmPermissions.VoidPayments, "Anular pagos", "Anular pagos y aprobar solicitudes de anulación."),
        (GsmPermissions.ViewFinances, "Ver finanzas", "Ingresos, cierre diario y cajas de otros."),
    ];

    public static string Profile(string? value)
        => Profiles.FirstOrDefault(p => p.Value == value).Label ?? value ?? "—";

    /// <summary>"Entrenador" o "Entrenador (ajustado)" cuando sus permisos difieren de los del perfil.</summary>
    public static string ProfileLabel(string? profile, bool isAdjusted)
        => isAdjusted ? $"{Profile(profile)} (ajustado)" : Profile(profile);

    public static string Permission(string? value)
        => Permissions.FirstOrDefault(p => p.Value == value).Label ?? value ?? "—";

    /// <summary>Frase de una fila de auditoría: "Ana cambió el perfil de Luis: Recepción → Entrenador".</summary>
    public static string AuditText(string action, string actor, string target, string? permission, string? oldValue, string? newValue)
        => action switch
        {
            "invited" => $"{actor} dio acceso a {target} con el perfil {Profile(newValue)}",
            "profile_changed" => $"{actor} cambió el perfil de {target}: {Profile(oldValue)} → {Profile(newValue)}",
            "permission_granted" => $"{actor} le dio a {target} el permiso «{Permission(permission)}»",
            "permission_revoked" => $"{actor} le quitó a {target} el permiso «{Permission(permission)}»",
            "link_copied" => newValue == "login"
                ? $"{actor} copió el enlace de acceso de {target}"
                : $"{actor} copió el enlace de activación de {target}",
            "owner_added" => $"{actor} agregó a {target} como Owner",
            "owner_promoted" => $"{actor} ascendió a {target} de staff ({Profile(oldValue)}) a Owner",
            "owner_removed" => $"{actor} quitó a {target} como Owner",
            "owner_demoted" => $"{actor} dejó a {target} como staff ({Profile(newValue)}) en vez de Owner",
            _ => $"{actor} modificó el acceso de {target}",
        };
}
