namespace GSM.UI.Shared.Formatting;

/// <summary>Textos del botón de WhatsApp y del registro de contacto (Comunicaciones v2, §B).</summary>
public static class WhatsAppLabels
{
    public const string MissingPhone = "Falta el celular";

    /// <summary>Nombre del tipo de plantilla, tal como se muestra en el panel.</summary>
    public static string Kind(string kind) => kind switch
    {
        "ExpiringSoon" => "por vencer",
        "Expired" => "vencido",
        "AppInvitation" => "invitación a la app",
        "Birthday" => "cumpleaños",
        _ => kind,
    };

    /// <summary>Plantilla que corresponde al estado comercial de la lista; nulo si no hay una evidente (se ofrece el menú).</summary>
    public static string? KindForStatus(string? commercialStatus) => commercialStatus switch
    {
        "expiring" => "ExpiringSoon",
        "grace" or "expired" => "Expired",
        _ => null,
    };

    /// <summary>Título de la plantilla en el editor de Configuración.</summary>
    public static string TemplateTitle(string kind) => kind switch
    {
        "ExpiringSoon" => "Membresía por vencer",
        "Expired" => "Membresía vencida o en gracia",
        "AppInvitation" => "Invitación a la app",
        "Birthday" => "Cumpleaños",
        _ => kind,
    };

    /// <summary>"hace 5 min", "hace 3 h", "hace 1 día", "hace 2 días", "hace 3 meses".</summary>
    public static string Ago(DateTimeOffset instant, DateTimeOffset now)
    {
        var elapsed = now - instant;
        if (elapsed.TotalMinutes < 1)
        {
            return "ahora";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"hace {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"hace {(int)elapsed.TotalHours} h";
        }

        var days = (int)elapsed.TotalDays;
        if (days < 60)
        {
            return days == 1 ? "hace 1 día" : $"hace {days} días";
        }

        return $"hace {days / 30} meses";
    }

    /// <summary>"Ana contactó por WhatsApp · plantilla por vencer · hace 2 días".</summary>
    public static string ContactNote(string contactedByName, string kind, DateTimeOffset contactedAt, DateTimeOffset now)
    {
        var who = string.IsNullOrWhiteSpace(contactedByName) ? "Alguien del equipo" : FirstName(contactedByName);
        return $"{who} contactó por WhatsApp · plantilla {Kind(kind)} · {Ago(contactedAt, now)}";
    }

    private static string FirstName(string fullName) => fullName.Trim().Split(' ')[0];
}
