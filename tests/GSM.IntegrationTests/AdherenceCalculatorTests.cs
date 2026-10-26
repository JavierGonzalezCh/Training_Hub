using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Adherence;
using GSM.Domain.Routines;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>T6: cumplimiento de la rutina por semana de negocio (función pura, sin base de datos).</summary>
public sealed class AdherenceCalculatorTests
{
    private static readonly Guid Member = Guid.CreateVersion7();
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    [Fact]
    public void Week_starts_on_monday_for_any_day_including_sunday()
    {
        Assert.Equal(Monday, AdherenceCalculator.WeekStartOf(Day(0)));
        Assert.Equal(Monday, AdherenceCalculator.WeekStartOf(Day(6)));
        Assert.Equal(Day(7), AdherenceCalculator.WeekStartOf(Day(7)));
    }

    [Fact]
    public void Counts_done_missed_and_off_plan_days_without_raising_the_percentage()
    {
        var a = Line();
        var routine = Weekly(Monday, (1, a), (3, Line()), (5, Line()));
        // Lunes hecho, martes (descanso) fuera de plan, miércoles sin registrar; hoy es sábado.
        var logs = new[] { Log(0, a.Id), Log(1, Guid.CreateVersion7()) };

        var week = AdherenceCalculator.Weeks([routine], logs, Monday, 1, Day(5), withDaysForLast: true)[0];

        Assert.Equal(3, week.PlannedDays);
        Assert.Equal(1, week.DoneDays);
        Assert.Equal(3, week.DueDays);
        Assert.Equal(1, week.OffPlanDays);
        Assert.Equal(33, week.CompliancePercent);
        Assert.Equal(
            ["done", "offplan", "missed", "rest", "missed", "rest", "rest"],
            week.Days!.Select(d => d.Status));
    }

    [Fact]
    public void Future_days_do_not_count_against_the_percentage_and_today_only_counts_once_done()
    {
        var a = Line();
        var b = Line();
        var routine = Weekly(Monday, (1, a), (3, b), (5, Line()));

        // Miércoles por la mañana: lunes hecho, hoy aún no cuenta en contra.
        var morning = AdherenceCalculator.Weeks([routine], [Log(0, a.Id)], Monday, 1, Day(2), true)[0];
        Assert.Equal(3, morning.PlannedDays);
        Assert.Equal(1, morning.DueDays);
        Assert.Equal(100, morning.CompliancePercent);
        Assert.Equal("today", morning.Days![2].Status);
        Assert.Equal("upcoming", morning.Days![4].Status);

        // Al registrar hoy, hoy cuenta como hecho.
        var evening = AdherenceCalculator.Weeks(
            [routine], [Log(0, a.Id), Log(2, b.Id)], Monday, 1, Day(2), true)[0];
        Assert.Equal(2, evening.DoneDays);
        Assert.Equal(2, evening.DueDays);
    }

    [Fact]
    public void A_week_without_planned_days_has_no_percentage()
    {
        var routine = Weekly(Monday.AddDays(14), (1, Line()));

        var week = AdherenceCalculator.Weeks([routine], [], Monday, 1, Day(6), false)[0];

        Assert.Equal(0, week.PlannedDays);
        Assert.Null(week.CompliancePercent);
        Assert.Null(AdherenceCalculator.Percent([week]));
    }

    [Fact]
    public void Exercise_totals_ignore_lines_already_retired_on_that_date()
    {
        var kept = Line();
        var retired = new AdherenceLine(Guid.CreateVersion7(), Day(2));
        var day = new AdherenceDay(Guid.CreateVersion7(), 1, false, 1, Monday, null, "A", [kept, retired]);
        var routine = Routine(Monday, ScheduleMode.Weekly, day);

        // Lunes (antes del retiro): 2 líneas. Una semana después, solo la que sigue.
        var weeks = AdherenceCalculator.Weeks([routine], [Log(0, kept.Id)], Day(7), 2, Day(13), false);

        Assert.Equal(2, weeks[0].PlannedExercises);
        Assert.Equal(1, weeks[0].DoneExercises);
        Assert.Equal(1, weeks[1].PlannedExercises);
    }

    [Fact]
    public void A_routine_changed_mid_week_judges_each_date_with_the_routine_in_force()
    {
        var old = Weekly(Monday, (1, Line()), (3, Line()), (5, Line()));
        old = old with { DeactivatedOn = Day(2), IsActive = false };
        var replacement = Weekly(Day(3), (1, Line()), (4, Line()));

        var week = AdherenceCalculator.Weeks([old, replacement], [], Monday, 1, Day(6), true)[0];

        // Lunes y miércoles de la vieja; el jueves de la nueva (el viernes y el lunes ya no tocan).
        Assert.Equal(["missed", "rest", "missed", "missed", "rest", "rest", "rest"], week.Days!.Select(d => d.Status));
        Assert.Equal(3, week.PlannedDays);
    }

    [Fact]
    public void Rotating_routine_follows_its_cycle_and_planned_rest_does_not_count()
    {
        var a = Line();
        var routine = Routine(
            Monday,
            ScheduleMode.Rotating,
            new AdherenceDay(Guid.CreateVersion7(), 1, false, null, Monday, null, "A", [a]),
            new AdherenceDay(Guid.CreateVersion7(), 2, true, null, Monday, null, "Descanso", []));

        var week = AdherenceCalculator.Weeks([routine], [Log(0, a.Id)], Monday, 1, Day(6), true)[0];

        // Lunes, miércoles, viernes y domingo entrenan; el resto es descanso.
        Assert.Equal(4, week.PlannedDays);
        Assert.Equal(1, week.DoneDays);
        Assert.Equal(["done", "rest", "missed", "rest", "missed", "rest", "today"], week.Days!.Select(d => d.Status));
    }

    [Fact]
    public void Percent_of_several_weeks_adds_days_instead_of_averaging_percentages()
    {
        var a = Line();
        var routine = Weekly(Monday, (1, a), (3, Line()));
        // Semana 1: 1 de 2. Semana 2: 0 de 1 vencido (solo el lunes pasó).
        var weeks = AdherenceCalculator.Weeks([routine], [Log(0, a.Id)], Day(7), 2, Day(8), false);

        Assert.Equal(33, AdherenceCalculator.Percent(weeks));
    }

    [Fact]
    public void Self_assigned_routines_are_measured_but_flagged()
    {
        var routine = Weekly(Monday, (1, Line())) with { SelfAssigned = true };

        var week = AdherenceCalculator.Weeks([routine], [], Monday, 1, Day(6), false)[0];

        Assert.True(week.SelfAssigned);
        Assert.Equal(0, week.CompliancePercent);
    }

    [Fact]
    public void A_log_of_another_line_does_not_count_the_planned_day_as_done()
    {
        var planned = Line();
        var routine = Weekly(Monday, (1, planned));
        var foreign = Log(0, Guid.CreateVersion7());   // un registro del lunes, pero de una línea que no es de este día

        var week = AdherenceCalculator.Weeks([routine], [foreign], Monday, 1, Day(3), true)[0];

        Assert.Equal(1, week.PlannedDays);
        Assert.Equal(0, week.DoneDays);
        Assert.Equal(0, week.DoneExercises);
        Assert.Equal("missed", week.Days![0].Status);
        Assert.Equal(0, week.CompliancePercent);
    }

    [Fact]
    public void A_line_retired_later_still_counts_the_day_it_was_logged()
    {
        var kept = Line();
        var retired = new AdherenceLine(Guid.CreateVersion7(), Day(3));
        var day = new AdherenceDay(Guid.CreateVersion7(), 1, false, 1, Monday, null, "A", [kept, retired]);
        var routine = Routine(Monday, ScheduleMode.Weekly, day);

        var week = AdherenceCalculator.Weeks([routine], [Log(0, retired.Id)], Monday, 1, Day(6), false)[0];

        Assert.Equal(1, week.DoneDays);
    }

    [Fact]
    public void The_calendar_rule_is_shared_with_the_streak_calendar()
    {
        var routine = Weekly(Monday, (1, Line()), (3, Line()));
        var ordered = RoutineCalendar.Order([routine]);

        Assert.True(RoutineCalendar.Resolve(ordered, Day(0)).IsTraining);          // lunes
        var tuesday = RoutineCalendar.Resolve(ordered, Day(1));                    // semanal sin fila: descanso planificado
        Assert.False(tuesday.IsTraining);
        Assert.True(tuesday.IsPlannedRest);
        Assert.Null(RoutineCalendar.Resolve(ordered, Monday.AddDays(-1)).Routine); // antes de empezar no hay rutina
    }

    private static AdherenceLine Line() => new(Guid.CreateVersion7(), null);

    private static AdherenceLog Log(int offset, Guid lineId) => new(Member, Day(offset), lineId);

    private static AdherenceRoutine Weekly(DateOnly startsOn, params (int Weekday, AdherenceLine Line)[] days)
        => Routine(
            startsOn,
            ScheduleMode.Weekly,
            days.Select(d => new AdherenceDay(Guid.CreateVersion7(), d.Weekday, false, d.Weekday, startsOn, null, "D", [d.Line]))
                .ToArray());

    private static AdherenceRoutine Routine(DateOnly startsOn, ScheduleMode mode, params AdherenceDay[] days)
        => new(Member, Guid.CreateVersion7(), "Rutina", false, mode, startsOn, null, true, days);
}
