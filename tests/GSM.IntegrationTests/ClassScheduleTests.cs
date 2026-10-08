using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class ClassScheduleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // 2026-03-02 es lunes.
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public async Task Recurring_class_shows_in_future_weeks_without_materializing_any_session()
    {
        var world = await SeedAsync(Monday);

        Guid classId;
        using (var scope = Staff(world))
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(MonWed("Spinning"), default);
            classId = created.Id;
        }

        using (var scope = Staff(world))
        {
            var from = Monday.AddDays(21);
            var schedule = await scope.ServiceProvider.GetRequiredService<GetScheduleHandler>()
                .HandleAsync(from, from.AddDays(6), default);

            var dates = schedule.Entries.Where(e => e.ClassId == classId).Select(e => e.SessionDate).ToList();
            Assert.Equal([from, from.AddDays(2)], dates); // lunes y miércoles de esa semana
            Assert.All(schedule.Entries, e => Assert.False(e.IsMaterialized));

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(0, await db.Set<ClassSession>().CountAsync());
        }
    }

    [Fact]
    public async Task Cancelling_one_occurrence_only_touches_that_date_and_deactivation_stops_the_series()
    {
        var world = await SeedAsync(Monday);
        var wedWeek1 = Monday.AddDays(2);

        Guid classId;
        using (var scope = Staff(world))
        {
            classId = (await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(MonWed("Yoga"), default)).Id;

            await scope.ServiceProvider.GetRequiredService<CancelSessionHandler>()
                .HandleAsync(classId, wedWeek1, "Festivo", default);
        }

        // Baja lógica con efecto en la segunda semana.
        Repin(world, Monday.AddDays(7));
        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<DeactivateClassHandler>()
                .HandleAsync(classId, default);
        }

        using (var scope = Staff(world))
        {
            var schedule = await scope.ServiceProvider.GetRequiredService<GetScheduleHandler>()
                .HandleAsync(Monday, Monday.AddDays(20), default);
            var mine = schedule.Entries.Where(e => e.ClassId == classId).ToList();

            // Solo quedan las ocurrencias anteriores a deactivated_on: lunes y miércoles de la semana 1.
            Assert.Equal([Monday, wedWeek1], mine.Select(e => e.SessionDate).ToList());

            var wed = mine.Single(e => e.SessionDate == wedWeek1);
            Assert.Equal(ClassSessionStatus.Cancelled.ToString(), wed.Status);
            Assert.True(wed.IsMaterialized);
            Assert.Equal("Festivo", wed.CancellationReason);

            var mon = mine.Single(e => e.SessionDate == Monday);
            Assert.Equal(ClassSessionStatus.Scheduled.ToString(), mon.Status);
            Assert.False(mon.IsMaterialized);

            var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
            Assert.Equal(1, await db.Set<ClassSession>().CountAsync()); // solo la sesión cancelada, preservada
        }
    }

    [Fact]
    public async Task Member_schedule_is_403_when_show_classes_is_off()
    {
        var world = await SeedAsync(Monday);

        using (var scope = Staff(world))
        {
            await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(MonWed("Body Pump"), default);
        }

        await SetShowClassesAsync(world.GymId, false);
        using (var scope = AsMember(world))
        {
            await Assert.ThrowsAsync<ForbiddenDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<GetMyClassesHandler>()
                    .HandleAsync(Monday, Monday.AddDays(6), default));
        }

        await SetShowClassesAsync(world.GymId, true);
        using (var scope = AsMember(world))
        {
            var schedule = await scope.ServiceProvider.GetRequiredService<GetMyClassesHandler>()
                .HandleAsync(Monday, Monday.AddDays(6), default);
            Assert.NotEmpty(schedule.Entries);
        }
    }

    private static SaveClassRequest MonWed(string name)
        => new(name, "desc", "Ana", 15, new TimeOnly(7, 0), 45,
            IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday, ClassDayOfWeek.Wednesday], ScheduledDate: null);

    private IServiceScope Staff(World world)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(world.StaffId, world.GymId, UserRole.Staff);
        return scope;
    }

    private IServiceScope AsMember(World world)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(world.MemberUserId, world.GymId, UserRole.Member);
        return scope;
    }

    private void Repin(World world, DateOnly day)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(day);
        scope.Dispose();
    }

    private async Task SetShowClassesAsync(Guid gymId, bool value)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "update gyms.gym_feature_flags set show_classes = {0} where gym_id = {1};", value, gymId);
        scope.Dispose();
    }

    private async Task<World> SeedAsync(DateOnly today)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CS", $"cs-{suffix}");
        var staff = MakeUser($"cs-{suffix}-staff", UserRole.Staff, gym.Id);
        var memberUser = MakeUser($"cs-{suffix}-member", UserRole.Member, gym.Id);
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Socio", "Uno", $"doc-{suffix}", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(memberUser);
        db.Add(member);
        await db.SaveChangesAsync();

        scope.Dispose();
        return new World(gym.Id, staff.Id, member.Id, memberUser.Id);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local, UserRole role, Guid gymId)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(Guid GymId, Guid StaffId, Guid MemberId, Guid MemberUserId);
}
