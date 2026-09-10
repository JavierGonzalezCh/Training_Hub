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

    /// <summary>Instante mostrado en la hora local del gym.</summary>
    public static string Instant(DateTimeOffset instant)
        => TimeZoneInfo.ConvertTime(instant, GymZone).ToString("d MMM yyyy, HH:mm", Colombia);

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
