using GSM.Application.Memberships;
using GSM.Application.Memberships.Contracts;
using GSM.Application.MemberStreak;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// MS8 ("Mi membresía": historial, entradas de tiquetera y congelamientos) y L7 ("Mi progreso" → Racha: calendario,
/// mejor racha, congeladores y entradas que no contaron). Los handlers se invocan directo, como el miembro.
/// </summary>
public sealed class MyProgressAndMembershipsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    // ---------- calculador puro de la mejor racha ----------

    [Fact]
    public void Best_streak_without_a_routine_uses_the_classic_tolerance()
    {
        var dates = new[] { Today.AddDays(-30), Today.AddDays(-28), Today.AddDays(-27), Today.AddDays(-25), Today.AddDays(-5) };

        var best = BestStreakCalculator.Compute(dates, null, new HashSet<DateOnly>(), new HashSet<DateOnly>(), new HashSet<DateOnly>(), Today);

        Assert.Equal(4, best);
    }

    [Fact]
    public void Best_streak_with_a_calendar_counts_rest_days_and_is_cut_by_a_missed_training_day()
    {
        var start = Today.AddDays(-9);
        var training = new HashSet<DateOnly>();
        var rest = new HashSet<DateOnly>();
        for (var i = 0; i < 10; i++)
        {
            var day = start.AddDays(i);
            (i % 4 == 3 ? rest : training).Add(day);
        }

        // Entrena todo menos el offset 5; el 9 (hoy) no penaliza.
        var attended = training.Where(d => d != start.AddDays(5) && d != Today).ToList();

        var best = BestStreakCalculator.Compute(attended, start, training, rest, new HashSet<DateOnly>(), Today);

        // offsets 0,1,2,3(descanso),4 = 5 antes del fallo; después 6,7(descanso),8 = 3.
        Assert.Equal(5, best);
    }

    [Fact]
    public void A_frozen_day_does_not_cut_the_best_streak()
    {
        var start = Today.AddDays(-4);
        var training = Enumerable.Range(0, 5).Select(i => start.AddDays(i)).ToHashSet();
        var attended = training.Where(d => d != start.AddDays(2) && d != Today).ToList();

        var withoutFreeze = BestStreakCalculator.Compute(attended, start, training, new HashSet<DateOnly>(), new HashSet<DateOnly>(), Today);
        var withFreeze = BestStreakCalculator.Compute(attended, start, training, new HashSet<DateOnly>(), new HashSet<DateOnly> { start.AddDays(2) }, Today);

        Assert.Equal(2, withoutFreeze);
        Assert.Equal(3, withFreeze);
    }

    // ---------- MS8: Mi membresía ----------

    [Fact]
    public async Task My_memberships_lists_history_freezes_and_pass_entries_without_money()
    {
        var w = await SeedAsync();
        Guid timeId;
        using (var s = Scope(w, UserRole.Owner))
        {
            var sp = s.ServiceProvider;
            var planId = (await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest("Mensual", null, 80000m, 30, MaxFreezeDays: 10), default)).Id;
            var passPlanId = (await sp.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest("Bono 10", null, 50000m, 60, Kind: "pass", Entries: 10, ValidityDays: 60), default)).Id;

            var assign = sp.GetRequiredService<AssignMembershipHandler>();
            timeId = (await assign.HandleAsync(w.MemberId, new AssignMembershipRequest(planId, Today.AddDays(-60), "Histórica"), default)).Id;
            await assign.HandleAsync(w.MemberId, new AssignMembershipRequest(passPlanId, null), default);
            await sp.GetRequiredService<CreateFreezeHandler>().HandleAsync(
                w.MemberId, timeId, new CreateFreezeRequest(Today.AddDays(-50), Today.AddDays(-46), "Viaje"), default);
        }

        MyMembershipsResponse result;
        using (var s = Scope(w, UserRole.Member))
        {
            result = await s.ServiceProvider.GetRequiredService<GetMyMembershipsHandler>().HandleAsync(default);
        }

        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.Access);
        var pass = Assert.Single(result.Items, i => i.Kind == "pass");
        Assert.Equal(10, pass.EntriesTotal);
        Assert.Equal(0, pass.EntriesUsed);
        Assert.Equal(10, pass.EntriesLeft);
        var time = Assert.Single(result.Items, i => i.Kind == "time");
        var freeze = Assert.Single(time.Freezes);
        Assert.Equal("Viaje", freeze.Reason);
        Assert.Equal("ended", freeze.State);
        // El historial más reciente va primero.
        Assert.True(result.Items[0].StartDate >= result.Items[1].StartDate);
    }

    [Fact]
    public async Task My_memberships_shows_only_the_callers_own()
    {
        var w1 = await SeedAsync();
        var w2 = await SeedAsync();
        using (var s = Scope(w2, UserRole.Owner))
        {
            var planId = (await s.ServiceProvider.GetRequiredService<CreatePlanHandler>().HandleAsync(
                new CreatePlanRequest("Otro", null, 1000m, 30), default)).Id;
            await s.ServiceProvider.GetRequiredService<AssignMembershipHandler>()
                .HandleAsync(w2.MemberId, new AssignMembershipRequest(planId, null), default);
        }

        using var mine = Scope(w1, UserRole.Member);
        var result = await mine.ServiceProvider.GetRequiredService<GetMyMembershipsHandler>().HandleAsync(default);

        Assert.Empty(result.Items);
        Assert.Equal("no_plan", result.Access.Commercial);
    }

    [Fact]
    public async Task A_staff_account_that_is_not_a_member_gets_forbidden()
    {
        var w = await SeedAsync();
        using var s = Scope(w, UserRole.Owner, userId: Guid.CreateVersion7());

        await Assert.ThrowsAsync<ForbiddenDomainException>(
            () => s.ServiceProvider.GetRequiredService<GetMyMembershipsHandler>().HandleAsync(default));
    }

    // ---------- L7: calendario de racha ----------

    [Fact]
    public async Task Calendar_marks_trained_days_and_leaves_the_rest_without_activity()
    {
        var w = await SeedAsync();
        await AddVisitsAsync(w, Today.AddDays(-3), Today.AddDays(-2));

        var calendar = await CalendarAsync(w, null, null);

        Assert.Equal(2026, calendar.Year);
        Assert.Equal(10, calendar.Month);
        Assert.Equal(31, calendar.Days.Count);
        Assert.Equal("trained", calendar.Days.Single(d => d.Date == Today.AddDays(-3)).Kind);
        Assert.Equal("none", calendar.Days.Single(d => d.Date == Today.AddDays(-5)).Kind);
        Assert.Equal("none", calendar.Days.Single(d => d.Date == Today.AddDays(5)).Kind);
        Assert.Equal("free", calendar.TodayKind);
        Assert.False(calendar.HasRoutine);
        Assert.Equal(Today.AddDays(-3), calendar.HistoryStart);
        Assert.True(calendar.BestStreak >= calendar.CurrentStreak);
        Assert.Equal(2, calendar.BestStreak);
    }

    [Fact]
    public async Task Calendar_today_is_trained_after_a_visit_today()
    {
        var w = await SeedAsync();
        await AddVisitsAsync(w, Today);

        var calendar = await CalendarAsync(w, null, null);

        Assert.Equal("trained", calendar.TodayKind);
    }

    [Fact]
    public async Task Calendar_explains_entries_that_did_not_count()
    {
        var w = await SeedAsync();
        await AddVisitsAsync(w, Today.AddDays(-2));
        await AddUncountedVisitAsync(w, Today.AddDays(-1), GSM.Domain.Attendance.LocationCheck.Denied);
        await AddUncountedVisitAsync(w, Today.AddDays(-4), GSM.Domain.Attendance.LocationCheck.Outside);

        var calendar = await CalendarAsync(w, null, null);

        var denied = calendar.Days.Single(d => d.Date == Today.AddDays(-1));
        Assert.NotEqual("trained", denied.Kind);
        Assert.Contains("ubicación", denied.Note);
        var outside = calendar.Days.Single(d => d.Date == Today.AddDays(-4));
        Assert.Contains("fuera", outside.Note);
        Assert.Null(calendar.Days.Single(d => d.Date == Today.AddDays(-2)).Note);
    }

    [Fact]
    public async Task Calendar_gives_the_first_freeze_and_says_when_the_next_one_arrives()
    {
        var w = await SeedAsync();
        await AddVisitsAsync(w, Today.AddDays(-1));

        var calendar = await CalendarAsync(w, null, null);

        // La consulta concede el primero de forma perezosa; el segundo llega 15 días después.
        Assert.Equal(1, calendar.FreezesAvailable);
        Assert.Equal(Today.AddDays(15), calendar.NextFreezeOn);
        Assert.Empty(calendar.FreezesUsedOn);
    }

    [Fact]
    public async Task Calendar_can_look_at_a_previous_month_and_rejects_an_invalid_one()
    {
        var w = await SeedAsync();
        await AddVisitsAsync(w, new DateOnly(2026, 9, 20));

        var september = await CalendarAsync(w, 2026, 9);

        Assert.Equal(30, september.Days.Count);
        Assert.Equal("trained", september.Days.Single(d => d.Date == new DateOnly(2026, 9, 20)).Kind);
        await Assert.ThrowsAsync<ValidationDomainException>(() => CalendarAsync(w, 2026, 13));
    }

    // ---------- helpers ----------

    private sealed record World(Guid GymId, Guid MemberId, Guid UserId, Guid OwnerId);

    private IServiceScope Scope(World w, UserRole role, Guid? userId = null)
    {
        var scope = factory.CreateScopeWithMigration();
        var user = scope.ServiceProvider.GetRequiredService<MutableCurrentUser>();
        user.ActAs(userId ?? (role == UserRole.Member ? w.UserId : w.OwnerId), w.GymId, role);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(Today);
        return scope;
    }

    private async Task<StreakCalendarResponse> CalendarAsync(World w, int? year, int? month)
    {
        using var s = Scope(w, UserRole.Member);
        return await s.ServiceProvider.GetRequiredService<GetMyStreakCalendarHandler>().HandleAsync(year, month, default);
    }

    private async Task AddVisitsAsync(World w, params DateOnly[] days)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        foreach (var day in days)
        {
            db.Add(AttendanceEntity.Create(
                w.GymId, w.MemberId, GSM.Domain.Attendance.AttendanceMethod.Manual,
                new DateTimeOffset(day.Year, day.Month, day.Day, 17, 0, 0, TimeSpan.Zero), day));
        }

        await db.SaveChangesAsync();
    }

    private async Task AddUncountedVisitAsync(World w, DateOnly day, GSM.Domain.Attendance.LocationCheck check)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var entry = AttendanceEntity.Create(
            w.GymId, w.MemberId, GSM.Domain.Attendance.AttendanceMethod.Manual,
            new DateTimeOffset(day.Year, day.Month, day.Day, 17, 0, 0, TimeSpan.Zero), day, locationCheck: check);
        db.Add(entry);
        await db.SaveChangesAsync();
    }

    private async Task<World> SeedAsync()
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        var gym = Gym.Create("Progreso Gym", $"mp-{Guid.NewGuid():N}"[..16]);
        var userId = Guid.CreateVersion7();
        var member = Member.Create(gym.Id, "Ana", "Progreso", $"doc-{Guid.NewGuid():N}"[..12], Today.AddYears(-30));
        member.LinkUser(userId);
        db.AddRange(gym, GymFeatureFlags.Default(gym.Id), member);
        await db.SaveChangesAsync();
        return new World(gym.Id, member.Id, userId, Guid.CreateVersion7());
    }
}
