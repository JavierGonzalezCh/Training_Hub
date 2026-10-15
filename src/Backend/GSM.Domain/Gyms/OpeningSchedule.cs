namespace GSM.Domain.Gyms;

/// <summary>Excepción de una fecha ya cargada, sin formato de persistencia.</summary>
public sealed record SpecialDayData(bool IsClosed, IReadOnlyList<TimeSlot> Slots);

/// <summary>Cómo está el gym en un instante: abierto, y hasta cuándo, o cerrado, y cuándo vuelve a abrir.</summary>
public sealed record GymOpenStatus(
    bool HasSchedule,
    bool IsOpen,
    TimeOnly? ClosesAt,
    DateOnly? NextOpensOn,
    TimeOnly? NextOpensAt,
    bool Is24Hours = false)
{
    /// <summary>Un gym sin horario cargado nunca rechaza nada.</summary>
    public static GymOpenStatus NoSchedule { get; } = new(false, true, null, null, null);
}

/// <summary>
/// Horario completo de un gym (semana, festivos y excepciones). Resuelve qué tramos valen en una fecha
/// y si el gym está abierto a una hora local. Todo en hora local del gym: quien llama convierte el instante.
/// </summary>
public sealed class OpeningSchedule(
    IReadOnlyDictionary<OpeningDay, IReadOnlyList<TimeSlot>> weekly,
    IReadOnlyDictionary<DateOnly, SpecialDayData> specialDays)
{
    /// <summary>Cuántos días hacia adelante se busca la próxima apertura.</summary>
    public const int LookAheadDays = 14;

    public bool HasSchedule => weekly.Values.Any(s => s.Count > 0);

    /// <summary>Tramos vigentes de una fecha: la excepción manda; luego el festivo; luego el día de la semana.</summary>
    public IReadOnlyList<TimeSlot> SlotsFor(DateOnly date)
    {
        if (specialDays.TryGetValue(date, out var special))
        {
            return special.IsClosed ? [] : special.Slots;
        }

        var day = ColombianHolidays.Find(date) is not null ? OpeningDay.Holiday : (OpeningDay)(int)date.DayOfWeek;
        return weekly.TryGetValue(day, out var slots) ? slots : [];
    }

    /// <summary>Tramos del horario semanal de un día de la semana, sin festivos ni excepciones. Vacío si ese día no abre.</summary>
    public IReadOnlyList<TimeSlot> SlotsForWeekday(OpeningDay day)
        => weekly.TryGetValue(day, out var slots) ? slots : [];

    public GymOpenStatus Evaluate(DateOnly date, TimeOnly time)
    {
        if (!HasSchedule)
        {
            return GymOpenStatus.NoSchedule;
        }

        var today = SlotsFor(date).OrderBy(s => s.Opens).ToList();
        var currentIndex = today.FindIndex(s => s.Contains(time));
        if (currentIndex >= 0)
        {
            if (today[currentIndex].IsAllDay)
            {
                return new GymOpenStatus(true, true, null, null, null, Is24Hours: true);
            }

            // Une tramos que se tocan (cierra 12:00 y abre 12:00) para informar el cierre real.
            var closes = today[currentIndex].Closes;
            for (var i = currentIndex + 1; i < today.Count && today[i].Opens <= closes; i++)
            {
                closes = today[i].Closes > closes ? today[i].Closes : closes;
            }

            return new GymOpenStatus(true, true, closes, null, null);
        }

        var later = today.Where(s => s.Opens > time).OrderBy(s => s.Opens).ToList();
        if (later.Count > 0)
        {
            return new GymOpenStatus(true, false, null, date, later[0].Opens);
        }

        for (var i = 1; i <= LookAheadDays; i++)
        {
            var d = date.AddDays(i);
            var slots = SlotsFor(d);
            if (slots.Count > 0)
            {
                return new GymOpenStatus(true, false, null, d, slots.Min(s => s.Opens));
            }
        }

        return new GymOpenStatus(true, false, null, null, null);
    }
}
