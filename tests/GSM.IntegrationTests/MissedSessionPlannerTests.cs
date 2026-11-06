using GSM.Application.Routines;
using GSM.Application.Routines.Abstractions;
using GSM.Application.Routines.Adherence;
using GSM.Application.Routines.MissedSession;
using GSM.Domain.Routines;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>SP2–SP4: sesión perdida, cambio y reorganización de la semana (funciones puras, sin base de datos).</summary>
public sealed class MissedSessionPlannerTests
{
    private static readonly Guid Member = Guid.CreateVersion7();
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static DateOnly Day(int offset) => Monday.AddDays(offset);

    // Plan del documento: L Pecho · M Pierna · X Espalda · J Descanso · V Hombro (sábado y domingo sin fila).
    private static readonly AdherenceDay Chest = TrainingDay(1, "Pecho");
    private static readonly AdherenceDay Legs = TrainingDay(2, "Pierna");
    private static readonly AdherenceDay Back = TrainingDay(3, "Espalda");
    private static readonly AdherenceDay Rest = new(Guid.CreateVersion7(), 4, true, 4, Monday.AddDays(-7), null, "Descanso", []);
    private static readonly AdherenceDay Shoulders = TrainingDay(5, "Hombro");

    private static readonly AdherenceRoutine Plan = new(
        Member, Guid.CreateVersion7(), "Fuerza", false, ScheduleMode.Weekly, Monday.AddDays(-7), null, true,
        [Chest, Legs, Back, Rest, Shoulders]);

    private static AdherenceDay TrainingDay(int weekday, string name)
        => new(Guid.CreateVersion7(), weekday, false, weekday, Monday.AddDays(-7), null, name,
            [new AdherenceLine(Guid.CreateVersion7(), null)]);

    private static AdherenceLog Log(int offset, AdherenceDay day) => new(Member, Day(offset), day.Lines[0].Id);

    private static MissedSessionPlan? Detect(
        DateOnly today, IReadOnlyList<AdherenceLog>? logs = null, IReadOnlyList<AdherenceOverride>? overrides = null,
        AdherenceRoutine? routine = null)
        => MissedSessionPlanner.Detect(
            RoutineCalendar.Order([routine ?? Plan]), logs ?? [], RoutineCalendar.ByDate(overrides ?? []), today);

    private static AdherenceOverride Override(int offset, AdherenceDay planned, AdherenceDay performed)
        => new(Member, Plan.Id, Day(offset), planned.Id, performed.Id, RoutineDayOverrideReason.Swap);

    [Fact]
    public void Missing_monday_offers_it_on_tuesday_with_both_options_as_in_the_document()
    {
        var plan = Detect(Day(1))!;

        Assert.Equal(Day(0), plan.MissedOn);
        Assert.Equal(Chest.Id, plan.MissedDay.Id);
        Assert.False(plan.TodayIsRest);
        Assert.Equal(["Pierna", "Espalda", "Descanso", "Hombro", "Descanso", "Descanso"], plan.Before.Select(s => s.DayName));

        Assert.Equal(["Pecho", "Espalda", "Descanso", "Hombro"], plan.Swap!.Week.Take(4).Select(s => s.DayName));
        Assert.Equal("Pierna", plan.Swap.LostDayName);

        Assert.Equal(["Pecho", "Pierna", "Descanso", "Espalda"], plan.Reorganize!.Week.Take(4).Select(s => s.DayName));
        Assert.Equal("Hombro", plan.Reorganize.LostDayName);
    }

    [Fact]
    public void Changes_only_write_the_dates_that_move()
    {
        var plan = Detect(Day(1))!;

        var swap = MissedSessionPlanner.Changes(plan.Swap!);
        Assert.Equal([new PlannedOverride(Day(1), Legs.Id, Chest.Id)], swap);

        var reorganize = MissedSessionPlanner.Changes(plan.Reorganize!);
        Assert.Equal(
            [
                new PlannedOverride(Day(1), Legs.Id, Chest.Id),
                new PlannedOverride(Day(2), Back.Id, Legs.Id),
                new PlannedOverride(Day(4), Shoulders.Id, Back.Id),
            ],
            reorganize);
    }

    [Fact]
    public void Nothing_is_offered_when_yesterday_was_done_rest_or_today_already_started_or_was_decided()
    {
        Assert.Null(Detect(Day(1), [Log(0, Chest)]));          // ayer lo hizo
        Assert.Null(Detect(Day(4)));                           // ayer (jueves) era descanso
        Assert.Null(Detect(Day(1), [Log(1, Legs)]));           // hoy ya empezó con la de hoy (4a)
        Assert.Null(Detect(Day(1), overrides: [Override(1, Legs, Chest)])); // ya decidió
    }

    [Fact]
    public void Doing_another_day_yesterday_still_leaves_yesterday_missed()
    {
        var plan = Detect(Day(1), [Log(0, Back)]);

        Assert.NotNull(plan);
        Assert.Equal(Chest.Id, plan.MissedDay.Id);
    }

    [Fact]
    public void A_rest_day_today_only_suggests_doing_the_missed_one()
    {
        // Faltó el miércoles (Espalda); el jueves descansa.
        var plan = Detect(Day(3))!;

        Assert.True(plan.TodayIsRest);
        Assert.Equal(Back.Id, plan.MissedDay.Id);
        Assert.Null(plan.Swap);
        Assert.Null(plan.Reorganize);
    }

    [Fact]
    public void A_rotating_routine_can_only_swap()
    {
        var a = new AdherenceDay(Guid.CreateVersion7(), 1, false, null, Monday, null, "A", [new AdherenceLine(Guid.CreateVersion7(), null)]);
        var b = new AdherenceDay(Guid.CreateVersion7(), 2, false, null, Monday, null, "B", [new AdherenceLine(Guid.CreateVersion7(), null)]);
        var rotating = new AdherenceRoutine(Member, Guid.CreateVersion7(), "Rot", false, ScheduleMode.Rotating, Monday, null, true, [a, b]);

        var plan = Detect(Day(1), routine: rotating)!;

        Assert.Equal(a.Id, plan.MissedDay.Id);
        Assert.NotNull(plan.Swap);
        Assert.Null(plan.Reorganize);
    }

    [Fact]
    public void The_week_ends_on_sunday()
    {
        // Faltó el viernes (Hombro); hoy sábado descansa y la semana termina mañana.
        var plan = Detect(Day(5))!;

        Assert.Equal([Day(5), Day(6)], plan.Before.Select(s => s.Date));
    }

    [Fact]
    public void Calendar_counts_the_swapped_day_with_its_own_logs()
    {
        var change = Override(1, Legs, Chest);
        var resolution = RoutineCalendar.Resolve(RoutineCalendar.Order([Plan]), Day(1), RoutineCalendar.ByDate([change]));

        Assert.Equal(Chest.Id, resolution.Day!.Id);
        Assert.Equal(Legs.Id, resolution.PlannedDay!.Id);
        Assert.True(resolution.IsTraining);
    }

    [Fact]
    public void Adherence_counts_the_swapped_day_as_done_and_keeps_yesterday_missed()
    {
        var week = AdherenceCalculator.Weeks(
            [Plan], [Log(1, Chest)], Monday, 1, Day(1), withDaysForLast: true, [Override(1, Legs, Chest)])[0];

        Assert.Equal("missed", week.Days![0].Status);
        Assert.Equal("done", week.Days[1].Status);
        Assert.Equal("Pecho", week.Days[1].DayName);
        Assert.Equal("Pierna", week.Days[1].PlannedDayName);
        Assert.True(week.Days[1].IsChanged);
        Assert.Equal(1, week.DoneDays);

        // Sin el cambio, el mismo registro no cumplía el martes (SP1: registro libre).
        var free = AdherenceCalculator.Weeks([Plan], [Log(1, Chest)], Monday, 1, Day(1), withDaysForLast: true)[0];
        Assert.Equal("today", free.Days![1].Status);
    }

    [Fact]
    public void An_override_onto_a_retired_day_or_another_routine_is_ignored()
    {
        var other = new AdherenceOverride(Member, Guid.CreateVersion7(), Day(1), Legs.Id, Chest.Id, RoutineDayOverrideReason.Swap);
        var resolution = RoutineCalendar.Resolve(RoutineCalendar.Order([Plan]), Day(1), RoutineCalendar.ByDate([other]));

        Assert.Equal(Legs.Id, resolution.Day!.Id);
        Assert.False(resolution.IsOverridden);
    }
}
