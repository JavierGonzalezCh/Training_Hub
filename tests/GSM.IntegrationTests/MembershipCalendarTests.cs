using GSM.Domain.Common.Exceptions;
using GSM.Domain.Memberships;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>MS3: aritmética pura de los meses del calendario (casos de borde de fin de mes y bisiesto).</summary>
public sealed class MembershipCalendarTests
{
    private static DateOnly End(DateOnly start, int months, int? cycleDay = null)
        => MembershipCalendar.ComputeEnd(start, DurationUnit.Months, months, cycleDay);

    [Fact]
    public void One_month_from_the_eighth_ends_on_the_seventh_of_next_month()
    {
        Assert.Equal(new DateOnly(2026, 10, 7), End(new DateOnly(2026, 9, 8), 1));
        Assert.Equal(new DateOnly(2026, 11, 7), End(new DateOnly(2026, 10, 8), 1));
    }

    [Fact]
    public void One_month_from_the_first_ends_on_the_last_day_of_the_month()
    {
        Assert.Equal(new DateOnly(2026, 9, 30), End(new DateOnly(2026, 9, 1), 1));
        Assert.Equal(new DateOnly(2027, 2, 28), End(new DateOnly(2027, 2, 1), 1));
    }

    [Fact]
    public void January_31st_plus_one_month_ends_on_february_27th_or_28th_in_a_leap_year()
    {
        Assert.Equal(new DateOnly(2027, 2, 27), End(new DateOnly(2027, 1, 31), 1));
        Assert.Equal(new DateOnly(2028, 2, 28), End(new DateOnly(2028, 1, 31), 1)); // 2028 es bisiesto
    }

    [Fact]
    public void Day_that_does_not_exist_in_the_target_month_uses_its_last_day()
    {
        // 30 de enero + 1 mes: el 30 de febrero no existe → el ciclo siguiente arranca el 28.
        Assert.Equal(new DateOnly(2027, 2, 27), End(new DateOnly(2027, 1, 30), 1));
        // 31 de marzo + 1 mes: abril tiene 30 días → ciclo siguiente el 30, termina el 29.
        Assert.Equal(new DateOnly(2027, 4, 29), End(new DateOnly(2027, 3, 31), 1));
    }

    [Fact]
    public void Multi_month_plans_count_calendar_months()
    {
        Assert.Equal(new DateOnly(2026, 12, 7), End(new DateOnly(2026, 9, 8), 3));
        Assert.Equal(new DateOnly(2027, 9, 7), End(new DateOnly(2026, 9, 8), 12));
        Assert.Equal(new DateOnly(2027, 4, 29), End(new DateOnly(2026, 10, 31), 6)); // abril: ciclo el 30
    }

    [Fact]
    public void Plans_in_days_are_unchanged()
    {
        Assert.Equal(
            new DateOnly(2026, 10, 7),
            MembershipCalendar.ComputeEnd(new DateOnly(2026, 9, 8), DurationUnit.Days, 30, null));
        Assert.Equal(
            new DateOnly(2027, 1, 31),
            MembershipCalendar.ComputeEnd(new DateOnly(2027, 1, 31), DurationUnit.Days, 1, null));
    }

    [Fact]
    public void Chained_cycle_keeps_the_original_day_instead_of_losing_days_month_after_month()
    {
        // Empieza el 31 de enero. Sin día de ciclo la cadena iría 31 → 28 → 28 → 28...
        var start = new DateOnly(2027, 1, 31);
        int? cycle = null;
        var ends = new List<DateOnly>();
        for (var i = 0; i < 4; i++)
        {
            var (end, cycleDay) = MembershipCalendar.Resolve(start, DurationUnit.Months, 1, cycle);
            ends.Add(end);
            cycle = cycleDay;
            start = end.AddDays(1);
        }

        Assert.Equal(
            [new DateOnly(2027, 2, 27), new DateOnly(2027, 3, 30), new DateOnly(2027, 4, 29), new DateOnly(2027, 5, 30)],
            ends);
        Assert.Equal(31, cycle);
    }

    [Fact]
    public void Inherited_cycle_day_is_dropped_when_the_new_start_does_not_match_it()
    {
        // La anterior tenía ciclo el 8 pero su fin se corrió (congelamiento): la nueva empieza el 13.
        Assert.Equal(13, MembershipCalendar.ResolveCycleDay(new DateOnly(2026, 10, 13), 8));
        // Coincide (el 8): se conserva.
        Assert.Equal(8, MembershipCalendar.ResolveCycleDay(new DateOnly(2026, 10, 8), 8));
        // Febrero no tiene 31: empezar el 28 con ciclo 31 lo conserva.
        Assert.Equal(31, MembershipCalendar.ResolveCycleDay(new DateOnly(2027, 2, 28), 31));
        // Sin herencia, el día de inicio.
        Assert.Equal(31, MembershipCalendar.ResolveCycleDay(new DateOnly(2027, 1, 31), null));
    }

    [Fact]
    public void Membership_create_stores_cycle_day_only_for_month_plans()
    {
        var gym = Guid.NewGuid();
        var months = Membership.Create(
            gym, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2027, 1, 31), 1,
            durationUnit: DurationUnit.Months);
        var days = Membership.Create(gym, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2027, 1, 31), 30);

        Assert.Equal(31, months.CycleDay);
        Assert.Equal(new DateOnly(2027, 2, 27), months.EndDate);
        Assert.Null(days.CycleDay);
        Assert.Equal(new DateOnly(2027, 3, 1), days.EndDate);
    }

    [Fact]
    public void Plan_validates_duration_per_unit()
    {
        var gym = Guid.NewGuid();
        var plan = MembershipPlan.Create(gym, "Trimestral", null, 100m, 3, DurationUnit.Months);
        Assert.Equal(DurationUnit.Months, plan.DurationUnit);
        Assert.Equal(3, plan.DurationValue);

        Assert.Throws<ValidationDomainException>(
            () => MembershipPlan.Create(gym, "Cero", null, 100m, 0, DurationUnit.Months));
        Assert.Throws<ValidationDomainException>(
            () => MembershipPlan.Create(gym, "Enorme", null, 100m, MembershipPlan.MaxMonths + 1, DurationUnit.Months));

        // Sin unidad el plan es por días, como antes.
        var legacy = MembershipPlan.Create(gym, "Mensual", null, 100m, 30);
        Assert.Equal(DurationUnit.Days, legacy.DurationUnit);
    }
}
