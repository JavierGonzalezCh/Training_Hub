using GSM.Client.Generated;

namespace GSM.UI.Shared.Formatting;

/// <summary>Etiquetas y textos del horario de atención, compartidos por el panel y la app del miembro.</summary>
public static class OpeningHoursLabels
{
    /// <summary>Orden en que se muestran los días: de lunes a domingo y, al final, los festivos.</summary>
    public static readonly IReadOnlyList<(string Day, string Label)> Days =
    [
        ("Monday", "Lunes"),
        ("Tuesday", "Martes"),
        ("Wednesday", "Miércoles"),
        ("Thursday", "Jueves"),
        ("Friday", "Viernes"),
        ("Saturday", "Sábado"),
        ("Sunday", "Domingo"),
        ("Holiday", "Festivos"),
    ];

    public static string Label(string day)
        => Days.FirstOrDefault(d => string.Equals(d.Day, day, StringComparison.OrdinalIgnoreCase)).Label ?? day;

    /// <summary>"05:00" pasa a "5:00"; "22:00" queda igual.</summary>
    public static string Hour(string? time)
        => TimeOnly.TryParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t)
            ? $"{t.Hour}:{t.Minute:00}"
            : time ?? string.Empty;

    /// <summary>"5:00–12:00 · 15:00–22:00", o "Cerrado" si no hay tramos.</summary>
    public static string Describe(IEnumerable<TimeSlotDto>? slots)
    {
        var list = (slots ?? []).ToList();
        return list.Count == 0
            ? "Cerrado"
            : string.Join(" · ", list.Select(s => $"{Hour(s.Opens)}–{Hour(s.Closes)}"));
    }
}
