using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Domain.Members;
using GSM.Domain.Memberships;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

/// <summary>C5: fecha de fin de la serie. Base de carril: gsm_test_clases.</summary>
public sealed class ClassSeriesEndTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // 2026-03-02 es lunes.
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static SaveClassRequest Series(string name, DateOnly? endsOn)
        => new(name, null, "Ana", 15, new TimeOnly(7, 0), 45,
            IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday, ClassDayOfWeek.Wednesday],
            ScheduledDate: null, EndsOn: endsOn);

    private async Task<(Guid ClassId, World World)> CreateAsync(DateOnly? endsOn)
    {
        var world = await SeedAsync(Monday);
        using var scope = Staff(world);
        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Series("Natación", endsOn), default);
        Assert.Equal(endsOn, created.EndsOn);
        return (created.Id, world);
    }

    private async Task<List<DateOnly>> DatesAsync(World world, Guid classId, DateOnly from, DateOnly to)
    {
        using var scope = Staff(world);
        var schedule = await scope.ServiceProvider.GetRequiredService<GetScheduleHandler>()
            .HandleAsync(from, to, default);
        return schedule.Entries.Where(e => e.ClassId == classId).Select(e => e.SessionDate).ToList();
    }

    [Fact]
    public async Task Series_is_projected_up_to_the_end_date_inclusive_and_no_further()
    {
        // Termina el miércoles de la semana 2: incluye ese día, excluye todo lo posterior.
        var endsOn = Monday.AddDays(9);
        var (classId, world) = await CreateAsync(endsOn);

        var dates = await DatesAsync(world, classId, Monday, Monday.AddDays(27));

        Assert.Equal(
            [Monday, Monday.AddDays(2), Monday.AddDays(7), Monday.AddDays(9)], dates);
    }

    [Fact]
    public async Task Series_without_end_date_keeps_running()
    {
        var (classId, world) = await CreateAsync(null);

        var dates = await DatesAsync(world, classId, Monday.AddDays(70), Monday.AddDays(76));

        Assert.Equal([Monday.AddDays(70), Monday.AddDays(72)], dates);
    }

    [Fact]
    public async Task End_date_before_the_start_is_422_and_one_off_class_cannot_carry_one()
    {
        var world = await SeedAsync(Monday);
        using var scope = Staff(world);
        var create = scope.ServiceProvider.GetRequiredService<CreateClassHandler>();

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            create.HandleAsync(Series("Mala", Monday.AddDays(-1)), default));

        await Assert.ThrowsAsync<ValidationDomainException>(() =>
            create.HandleAsync(new SaveClassRequest("Puntual", null, null, 10, new TimeOnly(7, 0), 45,
                IsRecurring: false, DaysOfWeek: null, ScheduledDate: Monday.AddDays(3),
                EndsOn: Monday.AddDays(10)), default));
    }

    [Fact]
    public async Task Editing_the_end_date_shortens_extends_or_clears_the_series()
    {
        var (classId, world) = await CreateAsync(Monday.AddDays(7));

        using (var scope = Staff(world))
        {
            var update = scope.ServiceProvider.GetRequiredService<UpdateClassHandler>();
            var shortened = await update.HandleAsync(classId, Series("Natación", Monday.AddDays(1)), default);
            Assert.Equal(Monday.AddDays(1), shortened.EndsOn);
        }

        Assert.Equal([Monday], await DatesAsync(world, classId, Monday, Monday.AddDays(13)));

        using (var scope = Staff(world))
        {
            var cleared = await scope.ServiceProvider.GetRequiredService<UpdateClassHandler>()
                .HandleAsync(classId, Series("Natación", null), default);
            Assert.Null(cleared.EndsOn);
        }

        Assert.Equal(4, (await DatesAsync(world, classId, Monday, Monday.AddDays(13))).Count);
    }

    [Fact]
    public async Task Cannot_book_a_session_after_the_series_ended_but_can_on_the_last_day()
    {
        var lastDay = Monday.AddDays(2); // miércoles
        var (classId, world) = await CreateAsync(lastDay);

        using var scope = AsMember(world);
        var book = scope.ServiceProvider.GetRequiredService<BookClassHandler>();

        var ok = await book.HandleAsync(classId, lastDay, default);
        Assert.Equal(ClassBookingStatus.Booked.ToString(), ok.Status);

        await Assert.ThrowsAnyAsync<DomainException>(() =>
            book.HandleAsync(classId, Monday.AddDays(7), default));
    }

    [Fact]
    public async Task Catalog_exposes_the_end_date_and_an_ended_series_is_not_reported_outside_hours()
    {
        var (classId, world) = await CreateAsync(Monday.AddDays(30));

        using var scope = Staff(world);
        var list = await scope.ServiceProvider.GetRequiredService<ListClassesHandler>().HandleAsync(true, default);
        Assert.Equal(Monday.AddDays(30), list.Single(c => c.Id == classId).EndsOn);

        var outside = await scope.ServiceProvider.GetRequiredService<ListClassesOutsideHoursHandler>()
            .HandleAsync(default);
        Assert.DoesNotContain(outside, o => o.ClassId == classId);
    }

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

    private async Task<World> SeedAsync(DateOnly today)
    {
        using var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        scope.ServiceProvider.GetRequiredService<MutableClock>().PinToBusinessDay(today);
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags, members.members restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var gym = Gym.Create("CE", $"ce-{suffix}");
        var staff = MakeUser($"ce-{suffix}-staff");
        var memberUser = MakeUser($"ce-{suffix}-member");
        gym.AssignOwner(staff.Id);
        var member = Member.Create(gym.Id, "Miembro", "Uno", $"doc-{suffix}", new DateOnly(2026, 1, 1));
        member.LinkUser(memberUser.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);
        db.Add(memberUser);
        db.Add(member);
        // Desde C1, reservar exige estar al día el día de la sesión.
        var plan = MembershipPlan.Create(gym.Id, "Mensual", null, 50000m, 30);
        db.Add(plan);
        db.Add(Membership.Create(gym.Id, member.Id, plan.Id, Monday.AddDays(-5), 90));
        await db.SaveChangesAsync();

        return new World(gym.Id, staff.Id, member.Id, memberUser.Id);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local)
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
