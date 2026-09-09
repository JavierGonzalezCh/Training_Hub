using GSM.Application.Classes;
using GSM.Application.Classes.Contracts;
using GSM.Domain.Classes;
using GSM.Domain.Common.Exceptions;
using GSM.Domain.Gyms;
using GSM.Domain.Identity;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSM.IntegrationTests;

public sealed class ClassDefinitionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Recurring_class_without_days_is_rejected_with_422()
    {
        var world = await SeedAsync();
        using var scope = Staff(world.GymA, world.StaffA);
        var handler = scope.ServiceProvider.GetRequiredService<CreateClassHandler>();

        var request = new SaveClassRequest(
            "Spinning", null, "Ana", 15, new TimeOnly(7, 0), 45,
            IsRecurring: true, DaysOfWeek: [], ScheduledDate: null);

        await Assert.ThrowsAsync<ValidationDomainException>(() => handler.HandleAsync(request, default));
    }

    [Fact]
    public async Task One_off_class_without_date_is_rejected_with_422()
    {
        var world = await SeedAsync();
        using var scope = Staff(world.GymA, world.StaffA);
        var handler = scope.ServiceProvider.GetRequiredService<CreateClassHandler>();

        var request = new SaveClassRequest(
            "Masterclass", null, null, null, new TimeOnly(18, 0), 60,
            IsRecurring: false, DaysOfWeek: [], ScheduledDate: null);

        await Assert.ThrowsAsync<ValidationDomainException>(() => handler.HandleAsync(request, default));
    }

    [Fact]
    public async Task Zero_capacity_is_rejected_but_null_capacity_is_accepted()
    {
        var world = await SeedAsync();
        using var scope = Staff(world.GymA, world.StaffA);
        var handler = scope.ServiceProvider.GetRequiredService<CreateClassHandler>();

        await Assert.ThrowsAsync<ValidationDomainException>(() => handler.HandleAsync(
            Recurring("Yoga", capacity: 0), default));

        var free = await handler.HandleAsync(Recurring("Yoga libre", capacity: null), default);
        Assert.Null(free.Capacity);
        Assert.True(free.IsRecurring);
        Assert.Equal(world.Today, free.StartsOn);
    }

    [Fact]
    public async Task Deactivating_sets_deactivated_on_and_hides_from_active_list()
    {
        var world = await SeedAsync();
        using var scope = Staff(world.GymA, world.StaffA);

        var created = await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
            .HandleAsync(Recurring("Pilates", 12), default);

        await scope.ServiceProvider.GetRequiredService<DeactivateClassHandler>()
            .HandleAsync(created.Id, default);

        var active = await scope.ServiceProvider.GetRequiredService<ListClassesHandler>()
            .HandleAsync(includeInactive: false, default);
        Assert.DoesNotContain(active, c => c.Id == created.Id);

        var all = await scope.ServiceProvider.GetRequiredService<ListClassesHandler>()
            .HandleAsync(includeInactive: true, default);
        Assert.Contains(all, c => c.Id == created.Id);

        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();
        var row = await db.Set<GymClass>().SingleAsync(c => c.Id == created.Id);
        Assert.False(row.IsActive);
        Assert.Equal(world.Today, row.DeactivatedOn);
    }

    [Fact]
    public async Task Classes_are_isolated_between_gyms()
    {
        var world = await SeedAsync();

        using (var scope = Staff(world.GymA, world.StaffA))
        {
            await scope.ServiceProvider.GetRequiredService<CreateClassHandler>()
                .HandleAsync(Recurring("Solo A", 10), default);
        }

        using (var scope = Staff(world.GymB, world.StaffB))
        {
            var list = await scope.ServiceProvider.GetRequiredService<ListClassesHandler>()
                .HandleAsync(includeInactive: true, default);
            Assert.Empty(list);
        }
    }

    private static SaveClassRequest Recurring(string name, int? capacity)
        => new(name, null, "Ana", capacity, new TimeOnly(7, 0), 45,
            IsRecurring: true, DaysOfWeek: [ClassDayOfWeek.Monday, ClassDayOfWeek.Wednesday], ScheduledDate: null);

    private IServiceScope Staff(Guid gymId, Guid staffId)
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAs(staffId, gymId, UserRole.Staff);
        return scope;
    }

    private async Task<World> SeedAsync()
    {
        var scope = factory.CreateScopeWithMigration();
        scope.ServiceProvider.GetRequiredService<MutableCurrentUser>().ActAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<GsmDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate table identity.users, gyms.gyms, gyms.gym_feature_flags restart identity cascade;");
        await db.Database.ExecuteSqlRawAsync("delete from classes.gym_classes;");

        var today = scope.ServiceProvider.GetRequiredService<MutableClock>().Today;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (gymA, staffA) = CreateGym(db, $"cls-a-{suffix}", "CLS A");
        var (gymB, staffB) = CreateGym(db, $"cls-b-{suffix}", "CLS B");
        await db.SaveChangesAsync();

        scope.Dispose();
        return new World(gymA, staffA, gymB, staffB, today);
    }

    private static (Guid GymId, Guid StaffId) CreateGym(GsmDbContext db, string slug, string name)
    {
        var gym = Gym.Create(name, slug);
        var staff = MakeUser($"{slug}-staff", gym.Id);
        gym.AssignOwner(staff.Id);

        db.Add(gym);
        db.Add(GymFeatureFlags.Default(gym.Id));
        db.Add(staff);

        return (gym.Id, staff.Id);
    }

    private static GSM.Infrastructure.Identity.AppUser MakeUser(string local, Guid gymId)
        => new()
        {
            UserName = $"{local}@test.local",
            Email = $"{local}@test.local",
            NormalizedUserName = $"{local}@TEST.LOCAL",
            NormalizedEmail = $"{local}@TEST.LOCAL",
            EmailConfirmed = true,
            FullName = local,
            Role = UserRole.Staff,
            GymId = gymId,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

    private sealed record World(Guid GymA, Guid StaffA, Guid GymB, Guid StaffB, DateOnly Today);
}
