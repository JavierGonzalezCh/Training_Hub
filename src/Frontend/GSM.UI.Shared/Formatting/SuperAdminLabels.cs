using MudBlazor;

namespace GSM.UI.Shared.Formatting;

/// <summary>Estado efectivo/manual del gym y plan comercial, en español (ver CLAUDE.md, notas de SuperAdmin).</summary>
public static class SuperAdminLabels
{
    /// <summary>Uso del límite de miembros: "142 / 150", o "142 / sin límite" si el plan no lo tiene.</summary>
    public static string MemberUsage(int counted, int? limit)
        => limit is { } max ? $"{counted} / {max}" : $"{counted} / sin límite";

    /// <summary>Proporción 0–1 del límite usada, o <c>null</c> sin límite.</summary>
    public static double? MemberUsageRatio(int counted, int? limit)
        => limit is { } max and > 0 ? Math.Clamp((double)counted / max, 0, 1) : null;

    public static string StatusLabel(string status) => status switch
    {
        "Trial" => "Prueba",
        "Active" => "Activo",
        "Overdue" => "Moroso",
        "TrialEnded" => "Prueba terminada",
        "Disabled" => "Deshabilitado",
        "Suspended" => "Suspendido",
        _ => status,
    };

    /// <summary>Estado de la suscripción como lo lee el Owner de un gym (S3-3), no el equipo de plataforma.</summary>
    public static string OwnerStatusLabel(string status) => status switch
    {
        "Trial" => "En prueba",
        "TrialEnded" => "Prueba terminada",
        "Active" => "Al día",
        "Overdue" => "En mora",
        "Disabled" => "Inhabilitado",
        "Suspended" => "Suspendido",
        _ => status,
    };

    /// <summary>Qué significa el estado para el Owner y qué hacer; vacío si no pide nada.</summary>
    public static string OwnerStatusHint(string status) => status switch
    {
        "Trial" => "Estás probando TrainingHub. Cuando termine la prueba, comunícate con TrainingHub para seguir.",
        "TrialEnded" => "Tu prueba terminó. Aún puedes trabajar con normalidad unos días; comunícate con TrainingHub para continuar.",
        "Overdue" => "Tu suscripción está vencida. Pasada la gracia el gym quedará solo en consulta.",
        "Disabled" => "El gym está inhabilitado: solo puedes consultar la información, no registrar cambios.",
        "Suspended" => "TrainingHub suspendió el gym. Comunícate con TrainingHub para resolverlo.",
        _ => string.Empty,
    };

    public static Color StatusColor(string status) => status switch
    {
        // "Activo" es un estado correcto, no informativo: verde. "Prueba" no pide ninguna
        // acción, así que se queda neutro en vez de competir con los estados que sí.
        "Active" => Color.Success,
        "Trial" => Color.Default,
        "Overdue" or "TrialEnded" => Color.Warning,
        "Disabled" or "Suspended" => Color.Error,
        _ => Color.Default,
    };
}
