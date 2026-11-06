using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Contracts;
using GSM.Domain.Routines;

namespace GSM.Application.Routines.Adherence;

/// <summary>
/// Cumplimiento de la rutina por semana de negocio (lunes a domingo). Función pura sobre los datos ya leídos: cada
/// fecha se juzga con la rutina y los días vigentes ese día, igual que la racha (R2b), así que cambiar la rutina
/// a mitad de semana no reescribe el pasado. Los días de la semana son de negocio, nunca UTC.
/// </summary>
public static class AdherenceCalculator
{
    public const int BehindThresholdPercent = 50;

    public static DateOnly WeekStartOf(DateOnly date) => date.AddDays(-(IsoWeekDay.Of(date) - 1));

    /// <summary>
    /// Las <paramref name="weekCount"/> semanas que terminan en la que empieza el <paramref name="lastWeekStart"/>.
    /// <paramref name="withDaysForLast"/> agrega el detalle día por día a la última.
    /// </summary>
    public static IReadOnlyList<AdherenceWeekItem> Weeks(
        IReadOnlyList<AdherenceRoutine> routines,
        IReadOnlyList<AdherenceLog> logs,
        DateOnly lastWeekStart,
        int weekCount,
        DateOnly today,
        bool withDaysForLast,
        IReadOnlyList<AdherenceOverride>? overrides = null)
    {
        // Misma regla que el calendario de la racha (RoutineCalendar): la más reciente, y a igual inicio la activa.
        var ordered = RoutineCalendar.Order(routines);
        var logsByDate = logs.ToLookup(l => l.PerformedOn);
        // Ajustes del miembro (SP): ese día cuenta con los registros del día que se hizo, no del planeado.
        var changes = RoutineCalendar.ByDate(overrides ?? []);

        var weeks = new List<AdherenceWeekItem>(weekCount);
        for (var i = weekCount - 1; i >= 0; i--)
        {
            var start = lastWeekStart.AddDays(-7 * i);
            weeks.Add(Week(ordered, logsByDate, changes, start, today, withDaysForLast && i == 0));
        }

        return weeks;
    }

    /// <summary>Porcentaje de un conjunto de semanas juntas (días hechos sobre días que ya tocaban); nulo si no hubo.</summary>
    public static int? Percent(IEnumerable<AdherenceWeekItem> weeks)
    {
        int done = 0, due = 0;
        foreach (var week in weeks)
        {
            done += week.DoneDays;
            due += week.DueDays;
        }

        return due == 0 ? null : (int)Math.Round(done * 100m / due, MidpointRounding.AwayFromZero);
    }

    private static AdherenceWeekItem Week(
        IReadOnlyList<AdherenceRoutine> routines,
        ILookup<DateOnly, AdherenceLog> logsByDate,
        IReadOnlyDictionary<DateOnly, AdherenceOverride> changes,
        DateOnly weekStart,
        DateOnly today,
        bool withDays)
    {
        int planned = 0, done = 0, due = 0, plannedExercises = 0, doneExercises = 0, offPlan = 0;
        var selfAssigned = false;
        var days = withDays ? new List<AdherenceDayItem>(7) : null;

        for (var offset = 0; offset < 7; offset++)
        {
            var date = weekStart.AddDays(offset);
            var logged = logsByDate[date].Select(l => l.RoutineExerciseId).ToHashSet();
            var resolution = RoutineCalendar.Resolve(routines, date, changes);
            var (routine, day) = (resolution.Routine, resolution.Day);

            if (!resolution.IsTraining)
            {
                var offPlanDay = logged.Count > 0;
                if (offPlanDay)
                {
                    offPlan++;
                }

                days?.Add(new AdherenceDayItem(
                    date, offPlanDay ? "offplan" : resolution.IsPlannedRest ? "rest" : "none", day?.Name, 0, 0));
                continue;
            }

            var lines = day!.Lines.Where(l => l.RetiredOn is null || date < l.RetiredOn).ToList();
            var doneLines = lines.Count(l => logged.Contains(l.Id));
            // Hecho = registró algo de las líneas de este día (también las que se retiraron después): un registro de otra
            // rutina, o de un día que no es este, no cuenta el día planeado.
            var isDone = day.Lines.Any(l => logged.Contains(l.Id));

            planned++;
            plannedExercises += lines.Count;
            doneExercises += doneLines;
            selfAssigned |= routine!.SelfAssigned;
            if (isDone)
            {
                done++;
            }

            if (isDone || date < today)
            {
                due++;
            }

            var status = isDone ? "done" : date < today ? "missed" : date == today ? "today" : "upcoming";
            days?.Add(new AdherenceDayItem(
                date, status, day.Name, lines.Count, doneLines, resolution.PlannedDay?.Name, resolution.IsOverridden));
        }

        int? percent = due == 0 ? null : (int)Math.Round(done * 100m / due, MidpointRounding.AwayFromZero);
        return new AdherenceWeekItem(
            weekStart, planned, done, due, plannedExercises, doneExercises, offPlan, percent, selfAssigned, days);
    }
}
