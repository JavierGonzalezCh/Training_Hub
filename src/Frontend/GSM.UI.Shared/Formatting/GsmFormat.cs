using System.Globalization;

namespace GSM.UI.Shared.Formatting;

/// <summary>
/// Formato transversal de la UI (ver <c>CLAUDE.md</c> y el plan de frontend):
/// COP sin decimales en <c>es-CO</c>; los <see cref="DateOnly"/> se muestran tal cual (son días
/// de negocio, no se convierten de UTC); los <see cref="DateTimeOffset"/> se muestran en la zona
/// del gym (America/Bogotá).
/// </summary>
public static class GsmFormat
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    private static readonly TimeZoneInfo GymZone = ResolveBogota();

    /// <summary>COP sin decimales, p. ej. <c>$ 85.000</c>.</summary>
    public static string Money(decimal amount)
        => amount.ToString("C0", Colombia);

    /// <summary>Día de negocio, p. ej. <c>5 sept 2026</c>. No se convierte de zona horaria.</summary>
    public static string Day(DateOnly day)
        => day.ToString("d MMM yyyy", Colombia);

    /// <summary>
    /// Día corto con día de la semana, p. ej. <c>lun 14 sept</c>. Para listas acotadas a una
    /// semana, donde repetir el año en cada fila es ruido.
    /// </summary>
    public static string DayCompact(DateOnly day)
        => day.ToString("ddd d MMM", Colombia);

    /// <summary>
    /// Rango de días colapsando lo que comparten, p. ej. <c>14 – 20 sept 2026</c> o
    /// <c>28 sept – 4 oct 2026</c>. Escrito entero, "14 sept 2026 – 20 sept 2026" no cabe
    /// en una línea de móvil y parte el encabezado en dos.
    /// </summary>
    public static string DayRange(DateOnly from, DateOnly to)
    {
        if (from.Year != to.Year)
        {
            return $"{Day(from)} – {Day(to)}";
        }

        // from.Day, no ToString("d"): un "d" suelto es el patrón de fecha corta de .NET
        // (14/9/2026), no el día del mes.
        return from.Month == to.Month
            ? $"{from.Day} – {to.ToString("d MMM yyyy", Colombia)}"
            : $"{from.ToString("d MMM", Colombia)} – {to.ToString("d MMM yyyy", Colombia)}";
    }

    /// <summary>Instante mostrado en la hora local del gym.</summary>
    public static string Instant(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, GymZone).ToString("d MMM yyyy, HH:mm", Colombia);

    /// <summary>Solo la hora local del gym, p. ej. <c>06:42</c>. Para listas acotadas a un día.</summary>
    public static string Time(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, GymZone).ToString("HH:mm", Colombia);

    /// <summary>
    /// Día de negocio de hoy en la zona del gym. Nunca desde UTC: entre las 19:00 y medianoche
    /// de Colombia el día UTC ya es el siguiente.
    /// </summary>
    public static DateOnly Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GymZone).DateTime);

    /// <summary>Hora actual (0–23) en la zona del gym.</summary>
    public static int CurrentHour()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, GymZone).Hour;

    /// <summary>Iniciales para avatares: primera letra del primer y del último nombre.</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }

    /// <summary>Fecha larga para cabeceras, p. ej. <c>domingo 4 de octubre</c>.</summary>
    public static string DayLong(DateOnly day)
        => day.ToString("dddd d 'de' MMMM", Colombia);

    private static TimeZoneInfo ResolveBogota()
    {
        foreach (var id in new[] { "America/Bogota", "SA Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("GMT-5", TimeSpan.FromHours(-5), "GMT-5", "GMT-5");
    }
}
