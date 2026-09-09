using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;

namespace GSM.Application.Classes;

/// <summary>
/// El horario es una proyección: para un rango de fechas se combinan las ocurrencias calculadas
/// (por <c>days_of_week</c> entre <c>starts_on</c> y <c>deactivated_on</c>, o la fecha única de una
/// clase puntual) con las sesiones ya materializadas, <b>que mandan siempre</b> — una sesión
/// cancelada o con instructor distinto pisa a la calculada. Función pura, sin estado.
/// </summary>
public static class ClassSchedule
{
    public static IReadOnlyList<ScheduleEntry> Project(
        IReadOnlyCollection<GymClass> classes,
        IReadOnlyCollection<ClassSession> sessions,
        DateOnly from,
        DateOnly to)
    {
        var classById = classes.ToDictionary(c => c.Id);
        var materialized = sessions
            .Where(s => s.SessionDate >= from && s.SessionDate <= to)
            .ToDictionary(s => (s.ClassId, s.SessionDate));

        var entries = new List<ScheduleEntry>();

        // 1. Ocurrencias calculadas, salvo las que ya tienen sesión materializada (esas mandan).
        foreach (var c in classes)
        {
            foreach (var date in OccurrencesOf(c, from, to))
            {
                if (materialized.ContainsKey((c.Id, date)))
                {
                    continue;
                }

                entries.Add(new ScheduleEntry(
                    c.Id, c.Name, c.Description, c.InstructorName, c.Capacity,
                    date, c.StartTime, c.DurationMinutes,
                    ClassSessionStatus.Scheduled.ToString(), null, IsMaterialized: false, SessionId: null));
            }
        }

        // 2. Sesiones materializadas del rango: mandan siempre, aunque ya no sean una ocurrencia calculada.
        foreach (var s in materialized.Values)
        {
            var name = classById.TryGetValue(s.ClassId, out var cls) ? cls.Name : "(clase eliminada)";
            var description = cls?.Description;

            entries.Add(new ScheduleEntry(
                s.ClassId, name, description, s.InstructorName, s.Capacity,
                s.SessionDate, s.StartTime, s.DurationMinutes,
                s.Status.ToString(), s.CancellationReason, IsMaterialized: true, s.Id));
        }

        return entries
            .OrderBy(e => e.SessionDate)
            .ThenBy(e => e.StartTime)
            .ThenBy(e => e.ClassName)
            .ToList();
    }

    /// <summary>Fechas del rango <c>[from, to]</c> en las que la clase <paramref name="c"/> ocurre según su definición.</summary>
    public static IEnumerable<DateOnly> OccurrencesOf(GymClass c, DateOnly from, DateOnly to)
    {
        if (!c.IsRecurring)
        {
            if (c.ScheduledDate is { } date && date >= from && date <= to && NotStopped(c, date))
            {
                yield return date;
            }

            yield break;
        }

        var days = c.DaysOfWeek.ToHashSet();
        var start = from < c.StartsOn ? c.StartsOn : from;

        for (var d = start; d <= to; d = d.AddDays(1))
        {
            if (NotStopped(c, d) && days.Contains(ToClassDay(d.DayOfWeek)))
            {
                yield return d;
            }
        }
    }

    /// <summary>La baja lógica detiene la recurrencia desde <c>deactivated_on</c> (esa fecha ya no ocurre).</summary>
    private static bool NotStopped(GymClass c, DateOnly date)
        => c.DeactivatedOn is not { } stop || date < stop;

    private static ClassDayOfWeek ToClassDay(System.DayOfWeek dow)
        => dow == System.DayOfWeek.Sunday ? ClassDayOfWeek.Sunday : (ClassDayOfWeek)((int)dow - 1);
}
