namespace GSM.Domain.Gyms;

public sealed record Holiday(DateOnly Date, string Name);

/// <summary>
/// Festivos de Colombia de un año: fijos, los que se trasladan al lunes siguiente (Ley Emiliani, Ley
/// 51 de 1983) y los que dependen de la Pascua. Cálculo puro, sin tablas que mantener cada año.
/// </summary>
public static class ColombianHolidays
{
    public static IReadOnlyList<Holiday> ForYear(int year)
    {
        var easter = EasterSunday(year);
        var list = new List<Holiday>
        {
            // Fijos.
            new(new DateOnly(year, 1, 1), "Año Nuevo"),
            new(new DateOnly(year, 5, 1), "Día del Trabajo"),
            new(new DateOnly(year, 7, 20), "Día de la Independencia"),
            new(new DateOnly(year, 8, 7), "Batalla de Boyacá"),
            new(new DateOnly(year, 12, 8), "Inmaculada Concepción"),
            new(new DateOnly(year, 12, 25), "Navidad"),

            // Se trasladan al lunes siguiente (si ya es lunes, se quedan).
            new(NextMonday(new DateOnly(year, 1, 6)), "Día de los Reyes Magos"),
            new(NextMonday(new DateOnly(year, 3, 19)), "Día de San José"),
            new(NextMonday(new DateOnly(year, 6, 29)), "San Pedro y San Pablo"),
            new(NextMonday(new DateOnly(year, 8, 15)), "Asunción de la Virgen"),
            new(NextMonday(new DateOnly(year, 10, 12)), "Día de la Raza"),
            new(NextMonday(new DateOnly(year, 11, 1)), "Día de Todos los Santos"),
            new(NextMonday(new DateOnly(year, 11, 11)), "Independencia de Cartagena"),

            // Semana Santa: jueves y viernes no se trasladan.
            new(easter.AddDays(-3), "Jueves Santo"),
            new(easter.AddDays(-2), "Viernes Santo"),

            // Dependen de la Pascua y se trasladan al lunes siguiente.
            new(NextMonday(easter.AddDays(39)), "Ascensión del Señor"),
            new(NextMonday(easter.AddDays(60)), "Corpus Christi"),
            new(NextMonday(easter.AddDays(68)), "Sagrado Corazón de Jesús"),
        };

        return list.OrderBy(h => h.Date).ToList();
    }

    public static Holiday? Find(DateOnly date)
        => ForYear(date.Year).FirstOrDefault(h => h.Date == date);

    /// <summary>El mismo día si es lunes; si no, el lunes siguiente.</summary>
    internal static DateOnly NextMonday(DateOnly date)
        => date.AddDays((8 - (int)date.DayOfWeek) % 7);

    /// <summary>Domingo de Pascua (calendario gregoriano, algoritmo de Meeus/Jones/Butcher).</summary>
    internal static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
