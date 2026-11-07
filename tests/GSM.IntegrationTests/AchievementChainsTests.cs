using GSM.Application.Achievements;
using GSM.Application.Achievements.Public;
using GSM.Application.Attendance.Public;
using GSM.Application.Routines;
using GSM.Domain.Achievements;
using GSM.Domain.Attendance;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AttendanceEntity = GSM.Domain.Attendance.Attendance;

namespace GSM.IntegrationTests;

/// <summary>
/// AC1: las condiciones nuevas de los logros (horarios, sesiones completas y ranking mensual), contra Postgres y con la
/// hora del gym (Colombia, UTC-5).
/// </summary>
public sealed class AchievementChainsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0)
        => new DateTimeOffset(year, month, day, hour, minute, 0, Colombia).ToUniversalTime();

    private IServiceScope System()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        return scope;
    }

    private IServiceScope Staff(Guid gymId, Guid staffId, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        return scope;
    }

    private async Task<(Guid GymId, Guid StaffId, Guid MemberId)> SeedAsync(params (AchievementConditionType Type, int Value)[] achievements)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var gym = Gym.Create("Chains " + suffix, $"ch-{suffix}");
        var staff = new GSM.Infrastructure.Identity.AppUser
        {
            UserName = $"staff-{suffix}@ach.local",
            Email = $"staff-{suffix}@ach.local",
            NormalizedUserName = $"STAFF-{suffix}@ACH.LOCAL".ToUpperInvariant(),
            NormalizedEmail = $"STAFF-{suffix}@ACH.LOCAL".ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = "Chains Staff",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Ana", "Constante", "doc-" + suffix, new DateOnly(2026, 1, 1),
            birthDate: new DateOnly(1990, 5, 10));

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(member);
        foreach (var (type, value) in achievements)
        {
            db.Add(Achievement.Create(gym.Id, $"{type} {value}", null, null, type, value));
        }

        await db.SaveChangesAsync();
        return (gym.Id, staff.Id, member.Id);
    }

    private async Task AddEntriesAsync(Guid gymId, Guid memberId, params DateTimeOffset[] instants)
    {
        using var scope = System();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        foreach (var at in instants)
        {
            db.Add(AttendanceEntity.Create(gymId, memberId, AttendanceMethod.Manual, at, DateOnly.FromDateTime(at.ToOffset(Colombia).DateTime)));
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Early_late_and_weekend_entries_are_counted_in_gym_time_and_unlock_their_achievements()
    {
        var (gymId, staffId, memberId) = await SeedAsync(
            (AchievementConditionType.EarlyVisits, 2),
            (AchievementConditionType.LateVisits, 1),
            (AchievementConditionType.Weekends, 2));

        await AddEntriesAsync(gymId, memberId,
            Local(2026, 11, 2, 6, 30),   // lunes temprano
            Local(2026, 11, 3, 7, 59),   // martes temprano
            Local(2026, 11, 4, 8, 0),    // miércoles a las 8:00: ya no es temprano
            Local(2026, 11, 5, 21, 15),  // jueves de noche (en UTC ya es viernes)
            Local(2026, 11, 7, 10, 0),   // sábado...
            Local(2026, 11, 8, 10, 0),   // ...y domingo del mismo fin de semana: cuenta 1
            Local(2026, 11, 14, 10, 0)); // otro sábado: 2 fines de semana

        using var scope = Staff(gymId, staffId, new DateOnly(2026, 11, 14));
        var habits = await scope.ServiceProvider.GetRequiredService<IAttendanceReader>().GetVisitHabitsAsync(memberId, default);
        Assert.Equal(new VisitHabits(2, 1, 2), habits);

        var unlocked = await scope.ServiceProvider.GetRequiredService<IAchievementService>().EvaluateForMemberAsync(memberId, default);
        Assert.Equal(3, unlocked.Count);
    }

    [Fact]
    public async Task Closing_the_month_records_the_positions_once_and_grants_the_ranking_achievements()
    {
        var (gymId, staffId, memberId) = await SeedAsync(
            (AchievementConditionType.MonthlyRankingTop, 1),
            (AchievementConditionType.MonthlyRankingTop, 10));
        Guid rivalId;
        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var rival = Member.Create(gymId, "Luis", "Rival", "doc-r-" + Guid.NewGuid().ToString("N")[..8], new DateOnly(2026, 1, 1),
                birthDate: new DateOnly(1988, 1, 1));
            db.Add(rival);
            db.Add(RankingParticipant.Join(gymId, memberId, false, null, DateTimeOffset.UtcNow));
            db.Add(RankingParticipant.Join(gymId, rival.Id, false, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            rivalId = rival.Id;
        }

        // Octubre: Ana 3 días, Luis 1 día.
        await AddEntriesAsync(gymId, memberId, Local(2026, 10, 5, 9), Local(2026, 10, 6, 9), Local(2026, 10, 7, 9));
        await AddEntriesAsync(gymId, rivalId, Local(2026, 10, 5, 9));

        using (var scope = Staff(gymId, staffId, new DateOnly(2026, 11, 2)))
        {
            var close = scope.ServiceProvider.GetRequiredService<CloseRankingMonthHandler>();
            var first = await close.HandleAsync(default, onlyGymId: gymId);
            var again = await close.HandleAsync(default, onlyGymId: gymId);

            Assert.Equal(new RankingCloseResult(1, 2), first);
            Assert.Equal(new RankingCloseResult(0, 0), again);
        }

        using (var scope = System())
        {
            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            var results = await db.Set<RankingMonthResult>().IgnoreQueryFilters()
                .Where(r => r.GymId == gymId).OrderBy(r => r.Position).ToListAsync();
            Assert.Equal([memberId, rivalId], results.Select(r => r.MemberId));
            Assert.All(results, r => Assert.Equal(new DateOnly(2026, 10, 1), r.Month));

            var anaUnlocks = await db.Set<MemberAchievement>().IgnoreQueryFilters().CountAsync(u => u.MemberId == memberId);
            var luisUnlocks = await db.Set<MemberAchievement>().IgnoreQueryFilters().CountAsync(u => u.MemberId == rivalId);
            Assert.Equal(2, anaUnlocks);  // primer puesto: top 1 y top 10
            Assert.Equal(1, luisUnlocks); // segundo: solo top 10
        }
    }

    [Fact]
    public void A_session_is_complete_only_when_every_line_in_force_of_a_day_was_logged()
    {
        var chest = Guid.NewGuid();
        var legs = Guid.NewGuid();
        var press = new DayLine(Guid.NewGuid(), chest, new DateOnly(2026, 10, 1), null);
        var fly = new DayLine(Guid.NewGuid(), chest, new DateOnly(2026, 10, 1), null);
        var retired = new DayLine(Guid.NewGuid(), chest, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1));
        var squat = new DayLine(Guid.NewGuid(), legs, new DateOnly(2026, 10, 1), null);
        var addedLater = new DayLine(Guid.NewGuid(), legs, new DateOnly(2026, 11, 10), null);
        var lines = new[] { press, fly, retired, squat, addedLater };

        var logs = new[]
        {
            // 2 nov: pecho completo (la línea retirada ya no se exige).
            new LoggedLine(new DateOnly(2026, 11, 2), press.Id),
            new LoggedLine(new DateOnly(2026, 11, 2), fly.Id),
            // 3 nov: pecho a medias.
            new LoggedLine(new DateOnly(2026, 11, 3), press.Id),
            // 4 nov: pierna completa (la línea agregada después todavía no existía).
            new LoggedLine(new DateOnly(2026, 11, 4), squat.Id),
            // 12 nov: pierna a medias (ya existe la línea nueva).
            new LoggedLine(new DateOnly(2026, 11, 12), squat.Id),
        };

        Assert.Equal(2, CompleteSessionCalculator.Count(logs, lines));
    }
}
